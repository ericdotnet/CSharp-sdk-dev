// Copyright (c) Cloud Native Foundation. 
// Licensed under the Apache 2.0 license.
// See LICENSE file in the project root for full license information.

namespace CloudNative.CloudEvents.UnitTests
{
    using System;
    using System.Net.Mime;
    using System.Text;
    using Xunit;
    using static TestHelpers;

    public class JsonTest
    {
        const string jsonv02 =
            "{\n" +
            "    \"specversion\" : \"0.2\",\n" +
            "    \"type\" : \"com.github.pull.create\",\n" +
            "    \"source\" : \"https://github.com/cloudevents/spec/pull/123\",\n" +
            "    \"id\" : \"A234-1234-1234\",\n" +
            "    \"time\" : \"2018-04-05T17:31:00Z\",\n" +
            "    \"comexampleextension1\" : \"value\",\n" +
            "    \"comexampleextension2\" : {\n" +
            "        \"othervalue\": 5\n" +
            "    },\n" +
            "    \"contenttype\" : \"text/xml\",\n" +
            "    \"data\" : \"<much wow=\\\"xml\\\"/>\"\n" +
            "}";

        const string jsonv10 =
            "{\n" +
            "    \"specversion\" : \"1.0\",\n" +
            "    \"type\" : \"com.github.pull.create\",\n" +
            "    \"source\" : \"https://github.com/cloudevents/spec/pull/123\",\n" +
            "    \"id\" : \"A234-1234-1234\",\n" +
            "    \"time\" : \"2018-04-05T17:31:00Z\",\n" +
            "    \"comexampleextension1\" : \"value\",\n" +
            "    \"datacontenttype\" : \"text/xml\",\n" +
            "    \"data\" : \"<much wow=\\\"xml\\\"/>\"\n" +
            "}";

        [Fact]
        public void ReserializeTest02()
        {
            var jsonFormatter = new JsonEventFormatter();
            var cloudEvent = jsonFormatter.DecodeStructuredEvent(Encoding.UTF8.GetBytes(jsonv02));
            var jsonData = jsonFormatter.EncodeStructuredEvent(cloudEvent, out var contentType);
            var cloudEvent2 = jsonFormatter.DecodeStructuredEvent(jsonData);

            Assert.Equal(cloudEvent2.SpecVersion, cloudEvent.SpecVersion);
            Assert.Equal(cloudEvent2.Type, cloudEvent.Type);
            Assert.Equal(cloudEvent2.Source, cloudEvent.Source);
            Assert.Equal(cloudEvent2.Id, cloudEvent.Id);
            AssertTimestampsEqual(cloudEvent2.Time, cloudEvent.Time);
            Assert.Equal(cloudEvent2.DataContentType, cloudEvent.DataContentType);
            Assert.Equal(cloudEvent2.Data, cloudEvent.Data);
        }

        [Fact]
        public void ReserializeTest10()
        {
            var jsonFormatter = new JsonEventFormatter();
            var cloudEvent = jsonFormatter.DecodeStructuredEvent(Encoding.UTF8.GetBytes(jsonv10));
            var jsonData = jsonFormatter.EncodeStructuredEvent(cloudEvent, out var contentType);
            var cloudEvent2 = jsonFormatter.DecodeStructuredEvent(jsonData);

            Assert.Equal(cloudEvent2.SpecVersion, cloudEvent.SpecVersion);
            Assert.Equal(cloudEvent2.Type, cloudEvent.Type);
            Assert.Equal(cloudEvent2.Source, cloudEvent.Source);
            Assert.Equal(cloudEvent2.Id, cloudEvent.Id);
            AssertTimestampsEqual(cloudEvent2.Time, cloudEvent.Time);
            Assert.Equal(cloudEvent2.DataContentType, cloudEvent.DataContentType);
            Assert.Equal(cloudEvent2.Data, cloudEvent.Data);
        }

        [Fact]
        public void ReserializeTestV0_2toV0_1()
        {
            var jsonFormatter = new JsonEventFormatter();
            var cloudEvent = jsonFormatter.DecodeStructuredEvent(Encoding.UTF8.GetBytes(jsonv02));
            cloudEvent = cloudEvent.WithSpecVersion(CloudEventsSpecVersion.V0_1);
            var jsonData = jsonFormatter.EncodeStructuredEvent(cloudEvent, out var contentType);
            var cloudEvent2 = jsonFormatter.DecodeStructuredEvent(jsonData);

            Assert.Equal(cloudEvent2.SpecVersion, cloudEvent.SpecVersion);
            Assert.Equal(cloudEvent2.Type, cloudEvent.Type);
            Assert.Equal(cloudEvent2.Source, cloudEvent.Source);
            Assert.Equal(cloudEvent2.Id, cloudEvent.Id);
            AssertTimestampsEqual(cloudEvent2.Time, cloudEvent.Time);
            Assert.Equal(cloudEvent2.DataContentType, cloudEvent.DataContentType);
            Assert.Equal(cloudEvent2.Data, cloudEvent.Data);
        }

        [Fact]
        public void ReserializeTestV1_0toV0_2()
        {
            var jsonFormatter = new JsonEventFormatter();
            var cloudEvent = jsonFormatter.DecodeStructuredEvent(Encoding.UTF8.GetBytes(jsonv10));
            cloudEvent = cloudEvent.WithSpecVersion(CloudEventsSpecVersion.V0_2);
            var jsonData = jsonFormatter.EncodeStructuredEvent(cloudEvent, out var contentType);
            var cloudEvent2 = jsonFormatter.DecodeStructuredEvent(jsonData);

            Assert.Equal(cloudEvent2.SpecVersion, cloudEvent.SpecVersion);
            Assert.Equal(cloudEvent2.Type, cloudEvent.Type);
            Assert.Equal(cloudEvent2.Source, cloudEvent.Source);
            Assert.Equal(cloudEvent2.Id, cloudEvent.Id);
            AssertTimestampsEqual(cloudEvent2.Time, cloudEvent.Time);
            Assert.Equal(cloudEvent2.DataContentType, cloudEvent.DataContentType);
            Assert.Equal(cloudEvent2.Data, cloudEvent.Data);
        }

        [Fact]
        public void StructuredParseSuccess02()
        {
            var jsonFormatter = new JsonEventFormatter();
            var cloudEvent = jsonFormatter.DecodeStructuredEvent(Encoding.UTF8.GetBytes(jsonv02));
            Assert.Equal(CloudEventsSpecVersion.V0_2, cloudEvent.SpecVersion);
            Assert.Equal("com.github.pull.create", cloudEvent.Type);
            Assert.Equal(new Uri("https://github.com/cloudevents/spec/pull/123"), cloudEvent.Source);
            Assert.Equal("A234-1234-1234", cloudEvent.Id);
            AssertTimestampsEqual("2018-04-05T17:31:00Z", cloudEvent.Time.Value);
            Assert.Equal(new ContentType(MediaTypeNames.Text.Xml), cloudEvent.DataContentType);
            Assert.Equal("<much wow=\"xml\"/>", cloudEvent.Data);

            var attr = cloudEvent.GetAttributes();
            Assert.Equal("value", (string)attr["comexampleextension1"]);
            Assert.Equal(5, (int)((dynamic)attr["comexampleextension2"]).othervalue);
        }

        [Fact]
        public void StructuredParseSuccess10()
        {
            var jsonFormatter = new JsonEventFormatter();
            var cloudEvent = jsonFormatter.DecodeStructuredEvent(Encoding.UTF8.GetBytes(jsonv10));
            Assert.Equal(CloudEventsSpecVersion.V1_0, cloudEvent.SpecVersion);
            Assert.Equal("com.github.pull.create", cloudEvent.Type);
            Assert.Equal(new Uri("https://github.com/cloudevents/spec/pull/123"), cloudEvent.Source);
            Assert.Equal("A234-1234-1234", cloudEvent.Id);
            AssertTimestampsEqual("2018-04-05T17:31:00Z", cloudEvent.Time.Value);
            Assert.Equal(new ContentType(MediaTypeNames.Text.Xml), cloudEvent.DataContentType);
            Assert.Equal("<much wow=\"xml\"/>", cloudEvent.Data);

            var attr = cloudEvent.GetAttributes();
            Assert.Equal("value", (string)attr["comexampleextension1"]);
        }

        [Fact]
        public void StructuredParseWithExtensionsSuccess02()
        {
            var jsonFormatter = new JsonEventFormatter();
            var cloudEvent = jsonFormatter.DecodeStructuredEvent(Encoding.UTF8.GetBytes(jsonv02), new ComExampleExtension1Extension(),
                new ComExampleExtension2Extension());
            Assert.Equal(CloudEventsSpecVersion.V0_2, cloudEvent.SpecVersion);
            Assert.Equal("com.github.pull.create", cloudEvent.Type);
            Assert.Equal(new Uri("https://github.com/cloudevents/spec/pull/123"), cloudEvent.Source);
            Assert.Equal("A234-1234-1234", cloudEvent.Id);
            AssertTimestampsEqual("2018-04-05T17:31:00Z", cloudEvent.Time.Value);
            Assert.Equal(new ContentType(MediaTypeNames.Text.Xml), cloudEvent.DataContentType);
            Assert.Equal("<much wow=\"xml\"/>", cloudEvent.Data);

            Assert.Equal("value", cloudEvent.Extension<ComExampleExtension1Extension>().ComExampleExtension1);
            Assert.Equal(5, cloudEvent.Extension<ComExampleExtension2Extension>().ComExampleExtension2.OtherValue);
        }

        [Fact]
        public void StructuredParseWithExtensionsSuccess10()
        {
            var jsonFormatter = new JsonEventFormatter();
            var cloudEvent = jsonFormatter.DecodeStructuredEvent(Encoding.UTF8.GetBytes(jsonv10), new ComExampleExtension1Extension());
            Assert.Equal(CloudEventsSpecVersion.V1_0, cloudEvent.SpecVersion);
            Assert.Equal("com.github.pull.create", cloudEvent.Type);
            Assert.Equal(new Uri("https://github.com/cloudevents/spec/pull/123"), cloudEvent.Source);
            Assert.Equal("A234-1234-1234", cloudEvent.Id);
            AssertTimestampsEqual("2018-04-05T17:31:00Z", cloudEvent.Time.Value);
            Assert.Equal(new ContentType(MediaTypeNames.Text.Xml), cloudEvent.DataContentType);
            Assert.Equal("<much wow=\"xml\"/>", cloudEvent.Data);

            Assert.Equal("value", cloudEvent.Extension<ComExampleExtension1Extension>().ComExampleExtension1);
        }
    }
}