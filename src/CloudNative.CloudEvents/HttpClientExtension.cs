﻿// Copyright (c) Cloud Native Foundation. 
// Licensed under the Apache 2.0 license.
// See LICENSE file in the project root for full license information.

namespace CloudNative.CloudEvents
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Mime;
    using System.Text;
    using System.Threading.Tasks;
    using Newtonsoft.Json;

    public static class HttpClientExtension
    {
        const string HttpHeaderPrefix = "ce-";

        const string SpecVersionHttpHeader1 = HttpHeaderPrefix + "cloudEventsVersion";

        const string SpecVersionHttpHeader2 = HttpHeaderPrefix + "specversion";

        static JsonEventFormatter jsonFormatter = new JsonEventFormatter();

        /// <summary>
        /// Copies the CloudEvent into this HttpListenerResponse instance
        /// </summary>
        /// <param name="httpListenerResponse">this</param>
        /// <param name="cloudEvent">CloudEvent to copy</param>
        /// <param name="contentMode">Content mode (structured or binary)</param>
        /// <param name="formatter">Formatter</param>
        /// <returns>Task</returns>
        public static Task CopyFromAsync(this HttpListenerResponse httpListenerResponse, CloudEvent cloudEvent,
            ContentMode contentMode, ICloudEventFormatter formatter)
        {
            if (contentMode == ContentMode.Structured)
            {
                var buffer =
                    formatter.EncodeStructuredEvent(cloudEvent, out var contentType);
                httpListenerResponse.ContentType = contentType.ToString();
                MapAttributesToListenerResponse(cloudEvent, httpListenerResponse);
                return httpListenerResponse.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }

            Stream stream = MapDataAttributeToStream(cloudEvent, formatter);
            httpListenerResponse.ContentType = cloudEvent.ContentType.ToString();
            MapAttributesToListenerResponse(cloudEvent, httpListenerResponse);
            return stream.CopyToAsync(httpListenerResponse.OutputStream);
        }

        /// <summary>
        /// Copies the CloudEvent into this HttpWebRequest instance
        /// </summary>
        /// <param name="httpWebRequest">this</param>
        /// <param name="cloudEvent">CloudEvent to copy</param>
        /// <param name="contentMode">Content mode (structured or binary)</param>
        /// <param name="formatter">Formatter</param>
        /// <returns>Task</returns>
        public static async Task CopyFromAsync(this HttpWebRequest httpWebRequest, CloudEvent cloudEvent,
            ContentMode contentMode, ICloudEventFormatter formatter)
        {
            if (contentMode == ContentMode.Structured)
            {
                var buffer =
                    formatter.EncodeStructuredEvent(cloudEvent, out var contentType);
                httpWebRequest.ContentType = contentType.ToString();
                MapAttributesToWebRequest(cloudEvent, httpWebRequest);
                await (httpWebRequest.GetRequestStream()).WriteAsync(buffer, 0, buffer.Length);
                return;
            }

            Stream stream = MapDataAttributeToStream(cloudEvent, formatter);
            httpWebRequest.ContentType = cloudEvent.ContentType.ToString();
            MapAttributesToWebRequest(cloudEvent, httpWebRequest);
            await stream.CopyToAsync(httpWebRequest.GetRequestStream());
        }

        /// <summary>
        /// Indicates whether this HttpResponseMessage holds a CloudEvent
        /// </summary>
        /// <param name="httpResponseMessage"></param>
        /// <returns>true, if the response is a CloudEvent</returns>
        public static bool IsCloudEvent(this HttpResponseMessage httpResponseMessage)
        {
            return (httpResponseMessage.Content.Headers.ContentType != null &&
                    httpResponseMessage.Content.Headers.ContentType.MediaType.StartsWith(CloudEvent.MediaType)) ||
                   httpResponseMessage.Headers.Contains(SpecVersionHttpHeader1) ||
                   httpResponseMessage.Headers.Contains(SpecVersionHttpHeader2);
        }

        /// <summary>
        /// Indicates whether this HttpListenerRequest holds a CloudEvent
        /// </summary>
        public static bool IsCloudEvent(this HttpListenerRequest httpListenerRequest)
        {
            return (httpListenerRequest.Headers["content-type"] != null &&
                    httpListenerRequest.Headers["content-type"].StartsWith(CloudEvent.MediaType)) ||
                   httpListenerRequest.Headers[SpecVersionHttpHeader1] != null ||
                   httpListenerRequest.Headers[SpecVersionHttpHeader2] != null;
        }

        /// <summary>
        /// Converts this response message into a CloudEvent object, with the given extensions.
        /// </summary>
        /// <param name="httpResponseMessage">Response message</param>
        /// <param name="extensions">List of extension instances</param>
        /// <returns>A CloudEvent instance or 'null' if the response message doesn't hold a CloudEvent</returns>
        public static CloudEvent ToCloudEvent(this HttpResponseMessage httpResponseMessage,
            params ICloudEventExtension[] extensions)
        {
            return ToCloudEventInternal(httpResponseMessage, null, extensions);
        }

        /// <summary>
        /// Converts this response message into a CloudEvent object, with the given extensions and
        /// overriding the default formatter.
        /// </summary>
        /// <param name="httpResponseMessage">Response message</param>
        /// <param name="formatter"></param>
        /// <param name="extensions">List of extension instances</param>
        /// <returns>A CloudEvent instance or 'null' if the response message doesn't hold a CloudEvent</returns>
        public static CloudEvent ToCloudEvent(this HttpResponseMessage httpResponseMessage,
            ICloudEventFormatter formatter, params ICloudEventExtension[] extensions)
        {
            return ToCloudEventInternal(httpResponseMessage, formatter, extensions);
        }

        /// <summary>
        /// Converts this listener request into a CloudEvent object, with the given extensions.
        /// </summary>
        /// <param name="httpListenerRequest">Listener request</param>
        /// <param name="extensions">List of extension instances</param>
        /// <returns>A CloudEvent instance or 'null' if the response message doesn't hold a CloudEvent</returns>
        public static CloudEvent ToCloudEvent(this HttpListenerRequest httpListenerRequest,
            params ICloudEventExtension[] extensions)
        {
            return ToCloudEvent(httpListenerRequest, null, extensions);
        }

        /// <summary>
        /// Converts this listener request into a CloudEvent object, with the given extensions,
        /// overriding the formatter.
        /// </summary>
        /// <param name="httpListenerRequest">Listener request</param>
        /// <param name="formatter"></param>
        /// <param name="extensions">List of extension instances</param>
        /// <returns>A CloudEvent instance or 'null' if the response message doesn't hold a CloudEvent</returns>
        public static CloudEvent ToCloudEvent(this HttpListenerRequest httpListenerRequest,
            ICloudEventFormatter formatter = null,
            params ICloudEventExtension[] extensions)
        {
            if (httpListenerRequest.ContentType != null &&
                httpListenerRequest.ContentType.StartsWith(CloudEvent.MediaType,
                    StringComparison.InvariantCultureIgnoreCase))
            {
                // handle structured mode
                if (formatter == null)
                {
                    // if we didn't get a formatter, pick one
                    if (httpListenerRequest.ContentType.EndsWith(JsonEventFormatter.MediaTypeSuffix,
                        StringComparison.InvariantCultureIgnoreCase))
                    {
                        formatter = jsonFormatter;
                    }
                    else
                    {
                        throw new InvalidOperationException("Unsupported CloudEvents encoding");
                    }
                }

                return formatter.DecodeStructuredEvent(httpListenerRequest.InputStream, extensions);
            }
            else
            {
                CloudEventsSpecVersion version = CloudEventsSpecVersion.Default;
                if (httpListenerRequest.Headers[SpecVersionHttpHeader1] != null)
                {
                    version = CloudEventsSpecVersion.V0_1;
                }

                if (httpListenerRequest.Headers[SpecVersionHttpHeader2] != null)
                {
                    version = httpListenerRequest.Headers[SpecVersionHttpHeader2] == "0.2"
                        ? CloudEventsSpecVersion.V0_2
                        : CloudEventsSpecVersion.Default;
                }

                var cloudEvent = new CloudEvent(version, extensions);
                var attributes = cloudEvent.GetAttributes();
                foreach (var httpRequestHeaders in httpListenerRequest.Headers.AllKeys)
                {
                    if (httpRequestHeaders.Equals(SpecVersionHttpHeader1,
                            StringComparison.InvariantCultureIgnoreCase) ||
                        httpRequestHeaders.Equals(SpecVersionHttpHeader2, StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }

                    if (httpRequestHeaders.StartsWith(HttpHeaderPrefix, StringComparison.InvariantCultureIgnoreCase))
                    {
                        string headerValue = httpListenerRequest.Headers[httpRequestHeaders];
                        if (headerValue.StartsWith("{") && headerValue.EndsWith("}") ||
                            headerValue.StartsWith("[") && headerValue.EndsWith("]"))
                        {
                            attributes[httpRequestHeaders.Substring(3).ToLowerInvariant()] =
                                JsonConvert.DeserializeObject(headerValue);
                        }
                        else
                        {
                            attributes[httpRequestHeaders.Substring(3).ToLowerInvariant()] = headerValue;
                            attributes[httpRequestHeaders.Substring(3).ToLowerInvariant()] = headerValue;
                        }
                    }
                }

                cloudEvent.ContentType = httpListenerRequest.ContentType != null
                    ? new ContentType(httpListenerRequest.ContentType)
                    : null;
                cloudEvent.Data = httpListenerRequest.InputStream;
                return cloudEvent;
            }
        }

        static void MapAttributesToListenerResponse(CloudEvent cloudEvent, HttpListenerResponse httpListenerResponse)
        {
            foreach (var attribute in cloudEvent.GetAttributes())
            {
                if (!attribute.Key.Equals(CloudEventAttributes.DataAttributeName(cloudEvent.SpecVersion)) &&
                    !attribute.Key.Equals(CloudEventAttributes.ContentTypeAttributeName(cloudEvent.SpecVersion)))
                {
                    if (attribute.Value is string)
                    {
                        httpListenerResponse.Headers.Add(HttpHeaderPrefix + attribute.Key,
                            attribute.Value.ToString());
                    }
                    else if (attribute.Value is DateTime)
                    {
                        httpListenerResponse.Headers.Add(HttpHeaderPrefix + attribute.Key,
                            ((DateTime)attribute.Value).ToString("o"));
                    }
                    else if (attribute.Value is Uri || attribute.Value is int)
                    {
                        httpListenerResponse.Headers.Add(HttpHeaderPrefix + attribute.Key,
                            attribute.Value.ToString());
                    }
                    else
                    {
                        httpListenerResponse.Headers.Add(HttpHeaderPrefix + attribute.Key,
                            Encoding.UTF8.GetString(jsonFormatter.EncodeAttribute(cloudEvent.SpecVersion, attribute.Key,
                                attribute.Value,
                                cloudEvent.Extensions.Values)));
                    }
                }
            }
        }

        static void MapAttributesToWebRequest(CloudEvent cloudEvent, HttpWebRequest httpWebRequest)
        {
            foreach (var attribute in cloudEvent.GetAttributes())
            {
                if (!attribute.Key.Equals(CloudEventAttributes.DataAttributeName(cloudEvent.SpecVersion)) &&
                    !attribute.Key.Equals(CloudEventAttributes.ContentTypeAttributeName(cloudEvent.SpecVersion)))
                {
                    if (attribute.Value is string)
                    {
                        httpWebRequest.Headers.Add(HttpHeaderPrefix + attribute.Key, attribute.Value.ToString());
                    }
                    else if (attribute.Value is DateTime)
                    {
                        httpWebRequest.Headers.Add(HttpHeaderPrefix + attribute.Key,
                            ((DateTime)attribute.Value).ToString("o"));
                    }
                    else if (attribute.Value is Uri || attribute.Value is int)
                    {
                        httpWebRequest.Headers.Add(HttpHeaderPrefix + attribute.Key, attribute.Value.ToString());
                    }
                    else
                    {
                        httpWebRequest.Headers.Add(HttpHeaderPrefix + attribute.Key,
                            Encoding.UTF8.GetString(jsonFormatter.EncodeAttribute(cloudEvent.SpecVersion, attribute.Key,
                                attribute.Value,
                                cloudEvent.Extensions.Values)));
                    }
                }
            }
        }

        static Stream MapDataAttributeToStream(CloudEvent cloudEvent, ICloudEventFormatter formatter)
        {
            Stream stream;
            if (cloudEvent.Data is byte[])
            {
                stream = new MemoryStream((byte[])cloudEvent.Data);
            }
            else if (cloudEvent.Data is string)
            {
                stream = new MemoryStream(Encoding.UTF8.GetBytes((string)cloudEvent.Data));
            }
            else if (cloudEvent.Data is Stream)
            {
                stream = (Stream)cloudEvent.Data;
            }
            else
            {
                stream = new MemoryStream(formatter.EncodeAttribute(cloudEvent.SpecVersion,
                    CloudEventAttributes.DataAttributeName(cloudEvent.SpecVersion),
                    cloudEvent.Data, cloudEvent.Extensions.Values));
            }

            return stream;
        }

        static CloudEvent ToCloudEventInternal(HttpResponseMessage httpResponseMessage,
            ICloudEventFormatter formatter, ICloudEventExtension[] extensions)
        {
            if (httpResponseMessage.Content.Headers.ContentType != null &&
                httpResponseMessage.Content.Headers.ContentType.MediaType.StartsWith("application/cloudevents",
                    StringComparison.InvariantCultureIgnoreCase))
            {
                // handle structured mode
                if (formatter == null)
                {
                    // if we didn't get a formatter, pick one
                    if (httpResponseMessage.Content.Headers.ContentType.MediaType.EndsWith("+json",
                        StringComparison.InvariantCultureIgnoreCase))
                    {
                        formatter = jsonFormatter;
                    }
                    else
                    {
                        throw new InvalidOperationException("Unsupported CloudEvents encoding");
                    }
                }

                return formatter.DecodeStructuredEvent(
                    httpResponseMessage.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult(),
                    extensions);
            }
            else
            {
                CloudEventsSpecVersion version = CloudEventsSpecVersion.Default;
                if (httpResponseMessage.Headers.Contains(SpecVersionHttpHeader1))
                {
                    version = CloudEventsSpecVersion.V0_1;
                }

                if (httpResponseMessage.Headers.Contains(SpecVersionHttpHeader2))
                {
                    version = httpResponseMessage.Headers.GetValues(SpecVersionHttpHeader2).First() == "0.2"
                        ? CloudEventsSpecVersion.V0_2
                        : CloudEventsSpecVersion.Default;
                }

                var cloudEvent = new CloudEvent(version, extensions);
                var attributes = cloudEvent.GetAttributes();
                foreach (var httpResponseHeader in httpResponseMessage.Headers)
                {
                    if (httpResponseHeader.Key.Equals(SpecVersionHttpHeader1,
                            StringComparison.InvariantCultureIgnoreCase) ||
                        httpResponseHeader.Key.Equals(SpecVersionHttpHeader2,
                            StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }

                    if (httpResponseHeader.Key.StartsWith(HttpHeaderPrefix,
                        StringComparison.InvariantCultureIgnoreCase))
                    {
                        string headerValue = httpResponseHeader.Value.First();
                        var name = httpResponseHeader.Key.Substring(3).ToLowerInvariant();

                        if (headerValue.StartsWith("\"") && headerValue.EndsWith("\"") ||
                            headerValue.StartsWith("'") && headerValue.EndsWith("'") ||
                            headerValue.StartsWith("{") && headerValue.EndsWith("}") ||
                            headerValue.StartsWith("[") && headerValue.EndsWith("]"))
                        {
                            attributes[name] = jsonFormatter.DecodeAttribute(version, name,
                                Encoding.UTF8.GetBytes(headerValue), extensions);
                        }
                        else
                        {
                            attributes[name] = headerValue;
                        }                  
                    }
                }

                cloudEvent.ContentType = httpResponseMessage.Content.Headers.ContentType != null
                    ? new ContentType(httpResponseMessage.Content.Headers.ContentType.ToString())
                    : null;
                cloudEvent.Data = httpResponseMessage.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                return cloudEvent;
            }
        }
    }
}