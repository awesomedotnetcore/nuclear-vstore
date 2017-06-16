﻿using Newtonsoft.Json.Linq;

using NuClear.VStore.Descriptors.Objects;
using NuClear.VStore.Descriptors.Templates;

namespace NuClear.VStore.Json
{
    public static class JTokenExtensions
    {
        public static IObjectElementValue AsObjectElementValue(this JToken valueToken, ElementDescriptorType elementDescriptorType)
        {
            switch (elementDescriptorType)
            {
                case ElementDescriptorType.PlainText:
                case ElementDescriptorType.FormattedText:
                    return valueToken.ToObject<TextElementValue>();
                case ElementDescriptorType.Image:
                    return valueToken.ToObject<ImageElementValue>();
                case ElementDescriptorType.Article:
                    return valueToken.ToObject<ArticleElementValue>();
                case ElementDescriptorType.FasComment:
                    return valueToken.ToObject<FasElementValue>();
                case ElementDescriptorType.Date:
                    return valueToken.ToObject<DateElementValue>();
                case ElementDescriptorType.Link:
                    return valueToken.ToObject<TextElementValue>();
                case ElementDescriptorType.Phone:
                    return valueToken.ToObject<PhoneElementValue>();
                default:
                    return null;
            }
        }
    }
}
