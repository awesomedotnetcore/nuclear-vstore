﻿using System;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace NuClear.VStore.Descriptors.Objects
{
    public sealed class ObjectDescriptor : IIdentifyable<long>, IVersionedObjectDescriptor
    {
        public long Id { get; set; }
        public string VersionId { get; set; }
        public int VersionIndex { get; set; }
        public DateTime LastModified { get; set; }
        public long TemplateId { get; set; }
        public string TemplateVersionId { get; set; }
        public Language Language { get; set; }
        public JObject Properties { get; set; }
        public IReadOnlyCollection<IObjectElementDescriptor> Elements { get; set; }
        public ObjectMetadata Metadata { get; set; }

        public sealed class ObjectMetadata
        {
            public string Author { get; set; }
            public string AuthorLogin { get; set; }
            public string AuthorName { get; set; }
            public IReadOnlyCollection<int> ModifiedElements { get; set; }
        }
    }
}