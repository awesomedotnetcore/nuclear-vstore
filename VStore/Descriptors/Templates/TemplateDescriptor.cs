﻿using System;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace NuClear.VStore.Descriptors.Templates
{
    public sealed class TemplateDescriptor : IIdentityable, IVersionedTemplateDescriptor
    {
        public long Id { get; set; }

        public string VersionId { get; set; }

        public DateTime LastModified { get; set; }

        public JObject Properties { get; set; }

        public IReadOnlyCollection<IElementDescriptor> Elements { get; set; }
    }
}