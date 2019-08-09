using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using NuClear.VStore.Descriptors;
using NuClear.VStore.Descriptors.Templates;

namespace NuClear.VStore.Json
{
    public sealed class ElementDescriptorJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(IElementDescriptor);

        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotSupportedException();

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
            ReadJson(reader, objectType, existingValue, serializer, out _);

        public IElementDescriptor ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer, out JObject loadedObject)
        {
            try
            {
                loadedObject = JObject.Load(reader);
            }
            catch (JsonReaderException ex)
            {
                throw new JsonSerializationException("Template element descriptor is not a valid JSON", ex);
            }

            var typeToken = loadedObject[Tokens.TypeToken];
            if (typeToken == null)
            {
                throw new JsonSerializationException($"Some template element doesn't contain '{Tokens.TypeToken}' property.");
            }

            var type = typeToken.ToString();
            if (!Enum.TryParse<ElementDescriptorType>(type, true, out var descriptorType) ||
                !Enum.IsDefined(typeof(ElementDescriptorType), descriptorType))
            {
                throw new JsonSerializationException($"Some template element has incorrect type '{type}'.");
            }

            return DeserializeElementDescriptor(loadedObject, descriptorType);
        }

        private static IElementDescriptor DeserializeElementDescriptor(JToken token, ElementDescriptorType descriptorType)
        {
            var templateCodeToken = token[Tokens.TemplateCodeToken];
            if (templateCodeToken == null)
            {
                throw new JsonSerializationException($"Some template element of type '{descriptorType.ToString()}' doesn't contain '{Tokens.TemplateCodeToken}' property.");
            }

            var templateCode = templateCodeToken.ToObject<int>();
            var propertiesToken = token[Tokens.PropertiesToken];
            if (propertiesToken == null)
            {
                throw new JsonSerializationException($"Template element with template code '{templateCode}' doesn't contain '{Tokens.PropertiesToken}' property.");
            }

            var properties = (JObject)propertiesToken;
            var constraintSet = token[Tokens.ConstraintsToken];
            if (constraintSet == null)
            {
                throw new JsonSerializationException($"Template element with template code '{templateCode}' doesn't contain '{Tokens.ConstraintsToken}' property.");
            }

            return new ElementDescriptor(descriptorType, templateCode, properties, DeserializeConstraintSet(templateCode, constraintSet, descriptorType));
        }

        private static ConstraintSet DeserializeConstraintSet(int templateCode, JToken token, ElementDescriptorType descriptorType)
        {
            var constraintSetItems = new List<ConstraintSetItem>();
            foreach (var item in token)
            {
                if (item.Type != JTokenType.Property)
                {
                    throw new JsonSerializationException($"Template element with template code '{templateCode}' has malformed constraints.");
                }

                var property = (JProperty)item;
                if (!Enum.TryParse<Language>(property.Name, true, out var language) ||
                    !Enum.IsDefined(typeof(Language), language))
                {
                    throw new JsonSerializationException($"Template element with template code '{templateCode}' has incorrect constraint language '{property.Name}'.");
                }

                IElementConstraints constraints;
                switch (descriptorType)
                {
                    case ElementDescriptorType.PlainText:
                        constraints = property.Value.ToObject<PlainTextElementConstraints>();
                        break;
                    case ElementDescriptorType.FormattedText:
                        constraints = property.Value.ToObject<FormattedTextElementConstraints>();
                        break;
                    case ElementDescriptorType.BitmapImage:
                        constraints = property.Value.ToObject<BitmapImageElementConstraints>();
                        break;
                    case ElementDescriptorType.VectorImage:
                        constraints = property.Value.ToObject<VectorImageElementConstraints>();
                        break;
                    case ElementDescriptorType.Article:
                        constraints = property.Value.ToObject<ArticleElementConstraints>();
                        break;
                    case ElementDescriptorType.FasComment:
                        constraints = property.Value.ToObject<PlainTextElementConstraints>();
                        break;
                    case ElementDescriptorType.Link:
                    case ElementDescriptorType.VideoLink:
                        constraints = property.Value.ToObject<LinkElementConstraints>();
                        break;
                    case ElementDescriptorType.Phone:
                        constraints = property.Value.ToObject<PhoneElementConstraints>();
                        break;
                    case ElementDescriptorType.Color:
                        constraints = property.Value.ToObject<ColorElementConstraints>();
                        break;
                    case ElementDescriptorType.CompositeBitmapImage:
                        constraints = property.Value.ToObject<CompositeBitmapImageElementConstraints>();
                        break;
                    case ElementDescriptorType.ScalableBitmapImage:
                        constraints = property.Value.ToObject<ScalableBitmapImageElementConstraints>();
                        break;
                    default:
                        throw new JsonSerializationException($"Template element with template code '{templateCode}' has unknown type {descriptorType.ToString()}.");
                }

                constraintSetItems.Add(new ConstraintSetItem(language, constraints));
            }

            if (constraintSetItems.Count < 1)
            {
                throw new JsonSerializationException($"Template element with template code '{templateCode}' has no constraints.");
            }

            return new ConstraintSet(constraintSetItems);
        }
    }
}
