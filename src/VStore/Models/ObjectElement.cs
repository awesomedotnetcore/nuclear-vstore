using System;
using System.Collections.Generic;

namespace NuClear.VStore.Models
{
    public class ObjectElement
    {
        public ObjectElement()
        {
            ObjectLinks = new HashSet<ObjectElementLink>();
        }

        public long Id { get; set; }
        public string VersionId { get; set; }
        public string Data { get; set; }
        public DateTime LastModified { get; set; }

        public virtual ICollection<ObjectElementLink> ObjectLinks { get; set; }
    }
}