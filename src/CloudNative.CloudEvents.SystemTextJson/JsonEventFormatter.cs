﻿// Copyright (c) Cloud Native Foundation.
// Licensed under the Apache 2.0 license.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CloudNative.CloudEvents.SystemTextJson
{
    /// <summary>
    /// Formatter that implements the JSON Event Format, using System.Text.Json for JSON serialization and deserialization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When encoding CloudEvent data, the behavior of this implementation depends on the data
    /// content type of the CloudEvent and the type of the <see cref="CloudEvent.Data"/> property value,
    /// following the rules below. Derived classes can specialize this behavior by overriding
    /// <see cref="EncodeStructuredModeData(CloudEvent, Utf8JsonWriter)"/> or <see cref="EncodeBinaryModeEventData(CloudEvent)"/>.
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// If the data value is null, the content is empty for a binary mode message, and neither the "data"
    /// nor "data_base64" property is populated in a structured mode message.
    /// </description></item>
    /// <item><description>
    /// If the data content type is absent or has a media type of "application/json", the data is encoded as JSON,
    /// using the <see cref="JsonSerializerOptions"/> passed into the constructor, or the default options.
    /// </description></item>
    /// <item><description>
    /// Otherwise, if the data content type has a media type beginning with "text/" and the data value is a string,
    /// the data is serialized as a string.
    /// </description></item>
    /// <item><description>
    /// Otherwise, if the data value is a byte array, it is serialized either directly as binary data
    /// (for binary mode messages) or as base64 data (for structured mode messages).
    /// </description></item>
    /// <item><description>
    /// Otherwise, the encoding operation fails.
    /// </description></item>
    /// </list>
    /// <para>
    /// When decoding CloudEvent data, this implementation uses the following rules:
    /// </para>
    /// <para>
    /// In a structured mode message, any data is either binary data within the "data_base64" property value,
    /// or is a JSON token as the "data" property value. Binary data is represented as a byte array.
    /// A JSON token is decoded as a string if is just a string value and the data content type is specified
    /// and has a media type beginning with "text/". A JSON token representing the null value always
    /// leads to a null data result. In any other situation, the JSON token is preserved as a <see cref="JsonElement"/>
    /// that can be used for further deserialization (e.g. to a specific CLR type). This behavior can be modified
    /// by overriding <see cref="DecodeStructuredModeDataBase64Property(JsonElement, CloudEvent)"/> and
    /// <see cref="DecodeStructuredModeDataProperty(JsonElement, CloudEvent)"/>.
    /// </para>
    /// <para>
    /// In a binary mode message, the data is parsed based on the content type of the message. When the content
    /// type is absent or has a media type of "application/json", the data is parsed as JSON, with the result as
    /// a <see cref="JsonElement"/> (or null if the data is empty). When the content type has a media type beginning
    /// with "text/", the data is parsed as a string. In all other cases, the data is left as a byte array.
    /// This behavior can be specialized by overriding <see cref="DecodeBinaryModeEventData(byte[], CloudEvent)"/>.
    /// </para>
    /// </remarks>
    public class JsonEventFormatter : CloudEventFormatter
    {
        private const string JsonMediaType = "application/json";
        private const string MediaTypeSuffix = "+json";

        /// <summary>
        /// The property name to use for base64-encoded binary data in a structured-mode message.
        /// </summary>
        protected const string DataBase64PropertyName = "data_base64";

        /// <summary>
        /// The property name to use for general data in a structured-mode message.
        /// </summary>
        protected const string DataPropertyName = "data";

        /// <summary>
        /// The options to use when serializing objects to JSON.
        /// </summary>
        protected JsonSerializerOptions SerializerOptions { get; }

        /// <summary>
        /// The options to use when parsing JSON documents.
        /// </summary>
        protected JsonDocumentOptions DocumentOptions { get; }

        /// <summary>
        /// Creates a JsonEventFormatter that uses a default <see cref="JsonSerializer"/>.
        /// </summary>
        public JsonEventFormatter() : this(null, default)
        {
        }

        /// <summary>
        /// Creates a JsonEventFormatter that uses the specified <see cref="JsonSerializer"/>
        /// to serialize objects as JSON.
        /// </summary>
        /// <param name="serializerOptions">The options to use when serializing objects to JSON. May be null.</param>
        /// <param name="documentOptions">The options to use when parsing JSON documents.</param>
        public JsonEventFormatter(JsonSerializerOptions serializerOptions, JsonDocumentOptions documentOptions)
        {
            SerializerOptions = serializerOptions;
            DocumentOptions = documentOptions;
        }

        public override async Task<CloudEvent> DecodeStructuredModeMessageAsync(Stream data, ContentType contentType, IEnumerable<CloudEventAttribute> extensionAttributes)
        {
            data = data ?? throw new ArgumentNullException(nameof(data));
            var encoding = contentType.GetEncoding();

            JsonDocument document;
            if (encoding is UTF8Encoding)
            {
                document = await JsonDocument.ParseAsync(data, DocumentOptions).ConfigureAwait(false);
            }
            else
            {
                using var reader = new StreamReader(data, encoding);
                string json = await reader.ReadToEndAsync().ConfigureAwait(false);
                document = JsonDocument.Parse(json, DocumentOptions);
            }
            using (document)
            {
                return DecodeJsonDocument(document, extensionAttributes);
            }
        }

        public override CloudEvent DecodeStructuredModeMessage(Stream data, ContentType contentType, IEnumerable<CloudEventAttribute> extensionAttributes)
        {
            data = data ?? throw new ArgumentNullException(nameof(data));
            var encoding = contentType.GetEncoding();
            JsonDocument document;
            if (encoding is UTF8Encoding)
            {
                document = JsonDocument.Parse(data, DocumentOptions);
            }
            else
            {
                using var reader = new StreamReader(data, encoding);
                string json = reader.ReadToEnd();
                document = JsonDocument.Parse(json, DocumentOptions);
            }
            using (document)
            {
                return DecodeJsonDocument(document, extensionAttributes);
            }
        }

        public override CloudEvent DecodeStructuredModeMessage(byte[] data, ContentType contentType, IEnumerable<CloudEventAttribute> extensionAttributes) =>
            DecodeStructuredModeMessage(new MemoryStream(data), contentType, extensionAttributes);

        private CloudEvent DecodeJsonDocument(JsonDocument document, IEnumerable<CloudEventAttribute> extensionAttributes = null)
        {
            if (!document.RootElement.TryGetProperty(CloudEventsSpecVersion.SpecVersionAttribute.Name, out var specVersionProperty)
                || specVersionProperty.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException($"Structured mode content does not represent a CloudEvent");
            }
            var specVersion = CloudEventsSpecVersion.FromVersionId(specVersionProperty.GetString())
                ?? throw new ArgumentException($"Unsupported CloudEvents spec version '{specVersionProperty.GetString()}'");

            var cloudEvent = new CloudEvent(specVersion, extensionAttributes);
            PopulateAttributesFromStructuredEvent(cloudEvent, document);
            PopulateDataFromStructuredEvent(cloudEvent, document);
            // "data" is always the parameter from the public method. It's annoying not to be able to use
            // nameof here, but this will give the appropriate result.
            return cloudEvent.ValidateForConversion("data");
        }

        private void PopulateAttributesFromStructuredEvent(CloudEvent cloudEvent, JsonDocument document)
        {
            foreach (var jsonProperty in document.RootElement.EnumerateObject())
            {
                var name = jsonProperty.Name;
                var value = jsonProperty.Value;

                // Skip the spec version attribute, which we've already taken account of.
                // Data is handled later, when everything else (importantly, the data content type)
                // has been populated.
                if (name == CloudEventsSpecVersion.SpecVersionAttribute.Name ||
                    name == DataBase64PropertyName ||
                    name == DataPropertyName)
                {
                    continue;
                }

                // For non-extension attributes, validate that the token type is as expected.
                // We're more forgiving for extension attributes: if an integer-typed extension attribute
                // has a value of "10" (i.e. as a string), that's fine. (If it has a value of "garbage",
                // that will throw in SetAttributeFromString.)
                ValidateTokenTypeForAttribute(cloudEvent.GetAttribute(name), value.ValueKind);

                // TODO: This currently performs more conversions than it really should, in the cause of simplicity.
                // We basically need a matrix of "attribute type vs token type" but that's rather complicated.

                string attributeValue = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.True => CloudEventAttributeType.Boolean.Format(true),
                    JsonValueKind.False => CloudEventAttributeType.Boolean.Format(false),
                    JsonValueKind.Null => null,
                    // Note: this will fail if the value isn't an integer, or is out of range for Int32.
                    JsonValueKind.Number => CloudEventAttributeType.Integer.Format(value.GetInt32()),
                    _ => throw new ArgumentException($"Invalid token type '{value.ValueKind}' for CloudEvent attribute")
                };
                if (attributeValue is null)
                {
                    continue;
                }
                // Note: we *could* infer an extension type of integer and Boolean, but not other extension types.
                // (We don't want to assume that everything that looks like a timestamp is a timestamp, etc.)
                // Stick to strings for consistency.
                cloudEvent.SetAttributeFromString(name, attributeValue);
            }
        }

        private void ValidateTokenTypeForAttribute(CloudEventAttribute attribute, JsonValueKind valueKind)
        {
            // We can't validate unknown attributes, don't check for extension attributes,
            // and null values will be ignored anyway.
            if (attribute is null || attribute.IsExtension || valueKind == JsonValueKind.Null)
            {
                return;
            }

            // This is deliberately written so that if a new attribute type is added without this being updated, we "fail valid".
            // (That should only happen in major versions anyway, but it's worth being somewhat forgiving here.)
            // TODO: Can we avoid hard-coding the strings here? We could potentially introduce an enum for attribute types.
            var valid = attribute.Type.Name switch
            {
                "Binary" => valueKind == JsonValueKind.String,
                "Boolean" => valueKind == JsonValueKind.True || valueKind == JsonValueKind.False,
                "Integer" => valueKind == JsonValueKind.Number,
                "String" => valueKind == JsonValueKind.String,
                "Timestamp" => valueKind == JsonValueKind.String,
                "URI" => valueKind == JsonValueKind.String,
                "URI-Reference" => valueKind == JsonValueKind.String,
                _ => true
            };
            if (!valid)
            {
                throw new ArgumentException($"Invalid token type '{valueKind}' for CloudEvent attribute '{attribute.Name}' with type '{attribute.Type}'");
            }
        }

        private void PopulateDataFromStructuredEvent(CloudEvent cloudEvent, JsonDocument document)
        {
            // Fetch data and data_base64 tokens, and treat null as missing.
            document.RootElement.TryGetProperty(DataPropertyName, out var dataElement);
            if (dataElement is JsonElement { ValueKind: JsonValueKind.Null })
            {
                dataElement = new JsonElement();
            }
            document.RootElement.TryGetProperty(DataBase64PropertyName, out var dataBase64Token);
            if (dataBase64Token is JsonElement { ValueKind: JsonValueKind.Null })
            {
                dataBase64Token = new JsonElement();
            }

            // If we don't have any data, we're done.
            if (dataElement.ValueKind == JsonValueKind.Undefined && dataBase64Token.ValueKind == JsonValueKind.Undefined)
            {
                return;
            }
            // We can't handle both properties being set.
            if (dataElement.ValueKind != JsonValueKind.Undefined && dataBase64Token.ValueKind != JsonValueKind.Undefined)
            {
                throw new ArgumentException($"Structured mode content cannot contain both '{DataPropertyName}' and '{DataBase64PropertyName}' properties.");
            }
            // Okay, we have exactly one non-null data/data_base64 property.
            // Decode it, potentially using overridden methods for specialization.
            if (dataBase64Token.ValueKind != JsonValueKind.Undefined)
            {
                DecodeStructuredModeDataBase64Property(dataBase64Token, cloudEvent);
            }
            else
            {
                DecodeStructuredModeDataProperty(dataElement, cloudEvent);
            }
        }

        /// <summary>
        /// Decodes the "data_base64" property provided within a structured-mode message,
        /// populating the <see cref="CloudEvent.Data"/> property accordingly.
        /// </summary>
        /// <param name="cloudEvent"></param>
        /// <remarks>
        /// <para>
        /// This implementation converts JSON string tokens to byte arrays, and fails for any other token type.
        /// </para>
        /// <para>
        /// Override this method to provide more specialized conversions.
        /// </para>
        /// </remarks>
        /// <param name="dataBase64Element">The "data_base64" property value within the structured-mode message. Will not be null, and will
        /// not have a null token type.</param>
        /// <param name="cloudEvent">The event being decoded. This should not be modified except to
        /// populate the <see cref="CloudEvent.Data"/> property, but may be used to provide extra
        /// information such as the data content type. Will not be null.</param>
        /// <returns>The data to populate in the <see cref="CloudEvent.Data"/> property.</returns>
        protected virtual void DecodeStructuredModeDataBase64Property(JsonElement dataBase64Element, CloudEvent cloudEvent)
        {
            if (dataBase64Element.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException($"Structured mode property '{DataBase64PropertyName}' must be a string, when present.");
            }
            cloudEvent.Data = dataBase64Element.GetBytesFromBase64();
        }

        /// <summary>
        /// Decodes the "data" property provided within a structured-mode message,
        /// populating the <see cref="CloudEvent.Data"/> property accordingly.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This implementation converts JSON string tokens to strings when the content type suggests
        /// that's appropriate, but otherwise returns the token directly.
        /// </para>
        /// <para>
        /// Override this method to provide more specialized conversions.
        /// </para>
        /// </remarks>
        /// <param name="dataElement">The "data" property value within the structured-mode message. Will not be null, and will
        /// not have a null token type.</param>
        /// <param name="cloudEvent">The event being decoded. This should not be modified except to
        /// populate the <see cref="CloudEvent.Data"/> property, but may be used to provide extra
        /// information such as the data content type. Will not be null.</param>
        /// <returns>The data to populate in the <see cref="CloudEvent.Data"/> property.</returns>
        protected virtual void DecodeStructuredModeDataProperty(JsonElement dataElement, CloudEvent cloudEvent) =>
            cloudEvent.Data = dataElement.ValueKind == JsonValueKind.String && cloudEvent.DataContentType?.StartsWith("text/") == true
                ? dataElement.GetString()
                : (object) dataElement.Clone(); // Deliberately cast to object to provide the conditional operator expression type.

        public override byte[] EncodeStructuredModeMessage(CloudEvent cloudEvent, out ContentType contentType)
        {
            cloudEvent = cloudEvent ?? throw new ArgumentNullException(nameof(cloudEvent));
            cloudEvent.ValidateForConversion(nameof(cloudEvent));

            contentType = new ContentType("application/cloudevents+json")
            {
                CharSet = Encoding.UTF8.WebName
            };

            var stream = new MemoryStream();
            var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WritePropertyName(CloudEventsSpecVersion.SpecVersionAttribute.Name);
            writer.WriteStringValue(cloudEvent.SpecVersion.VersionId);
            var attributes = cloudEvent.GetPopulatedAttributes();
            foreach (var keyValuePair in attributes)
            {
                var attribute = keyValuePair.Key;
                var value = keyValuePair.Value;
                writer.WritePropertyName(attribute.Name);
                // TODO: Maybe we should have an enum associated with CloudEventsAttributeType?
                if (attribute.Type == CloudEventAttributeType.Integer)
                {
                    writer.WriteNumberValue((int) value);
                }
                else if (attribute.Type == CloudEventAttributeType.Boolean)
                {
                    writer.WriteBooleanValue((bool) value);
                }
                else
                {
                    writer.WriteStringValue(attribute.Type.Format(value));
                }
            }

            if (cloudEvent.Data is object)
            {
                EncodeStructuredModeData(cloudEvent, writer);
            }
            writer.WriteEndObject();
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// Encodes structured mode data within a CloudEvent, writing it to the specified <see cref="JsonWriter"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This implementation follows the rules listed in the class remarks. Override this method
        /// to provide more specialized behavior, writing only <see cref="DataPropertyName"/> or
        /// <see cref="DataBase64PropertyName"/> properties.
        /// </para>
        /// </remarks>
        /// <param name="cloudEvent">The CloudEvent being encoded, which will have a non-null value for
        /// its <see cref="CloudEvent.Data"/> property.
        /// <paramref name="writer"/>The writer to serialize the data to. Will not be null.</param>
        /// <see cref="CloudEvent.Data"/>.</param>
        protected virtual void EncodeStructuredModeData(CloudEvent cloudEvent, Utf8JsonWriter writer)
        {
            ContentType dataContentType = new ContentType(cloudEvent.DataContentType ?? JsonMediaType);
            if (dataContentType.MediaType == JsonMediaType)
            {
                writer.WritePropertyName(DataPropertyName);
                JsonSerializer.Serialize(writer, cloudEvent.Data, SerializerOptions);
            }
            else if (cloudEvent.Data is string text && dataContentType.MediaType.StartsWith("text/"))
            {
                writer.WritePropertyName(DataPropertyName);
                writer.WriteStringValue(text);
            }
            else if (cloudEvent.Data is byte[] binary)
            {
                writer.WritePropertyName(DataBase64PropertyName);
                writer.WriteStringValue(Convert.ToBase64String(binary));
            }
            else
            {
                throw new ArgumentException($"{nameof(JsonEventFormatter)} cannot serialize data of type {cloudEvent.Data.GetType()} with content type '{cloudEvent.DataContentType}'");
            }
        }

        public override byte[] EncodeBinaryModeEventData(CloudEvent cloudEvent)
        {
            cloudEvent = cloudEvent ?? throw new ArgumentNullException(nameof(cloudEvent));
            cloudEvent.ValidateForConversion(nameof(cloudEvent));

            if (cloudEvent.Data is null)
            {
                return Array.Empty<byte>();
            }
            ContentType contentType = new ContentType(cloudEvent.DataContentType ?? JsonMediaType);
            if (contentType.MediaType == JsonMediaType)
            {
                var encoding = contentType.GetEncoding();
                if (encoding is UTF8Encoding)
                {
                    return JsonSerializer.SerializeToUtf8Bytes(cloudEvent.Data, SerializerOptions);
                }
                else
                {
                    return contentType.GetEncoding().GetBytes(JsonSerializer.Serialize(cloudEvent.Data, SerializerOptions));
                }
            }
            if (contentType.MediaType.StartsWith("text/") && cloudEvent.Data is string text)
            {
                return contentType.GetEncoding().GetBytes(text);
            }
            if (cloudEvent.Data is byte[] bytes)
            {
                return bytes;
            }
            throw new ArgumentException($"{nameof(JsonEventFormatter)} cannot serialize data of type {cloudEvent.Data.GetType()} with content type '{cloudEvent.DataContentType}'");
        }

        public override void DecodeBinaryModeEventData(byte[] value, CloudEvent cloudEvent)
        {
            value = value ?? throw new ArgumentNullException(nameof(value));
            cloudEvent = cloudEvent ?? throw new ArgumentNullException(nameof(cloudEvent));

            ContentType contentType = new ContentType(cloudEvent.DataContentType ?? JsonMediaType);

            Encoding encoding = contentType.GetEncoding();

            if (contentType.MediaType == JsonMediaType)
            {
                if (value.Length > 0)
                {
                    using JsonDocument document = encoding is UTF8Encoding
                        ? JsonDocument.Parse(value, DocumentOptions)
                        : JsonDocument.Parse(encoding.GetString(value), DocumentOptions);
                    // We have to clone the data so that we can dispose of the JsonDocument.
                    cloudEvent.Data = document.RootElement.Clone();
                }
                else
                {
                    cloudEvent.Data = null;
                }
            }
            else if (contentType.MediaType.StartsWith("text/") == true)
            {
                cloudEvent.Data = encoding.GetString(value);
            }
            else
            {
                cloudEvent.Data = value;
            }
        }
    }
}