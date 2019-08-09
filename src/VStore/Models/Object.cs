using System;
using System.Collections.Generic;

namespace NuClear.VStore.Models
{
    public class Object
    {
        public Object()
        {
            ElementLinks = new HashSet<ObjectElementLink>();
        }

        public long Id { get; set; }
        public string VersionId { get; set; }
        public int VersionIndex { get; set; }
        public long TemplateId { get; set; }
        public string TemplateVersionId { get; set; }
        public string Data { get; set; }
        public int[] ModifiedElements { get; set; }
        public DateTime LastModified { get; set; }
        public string Author { get; set; }
        public string AuthorLogin { get; set; }
        public string AuthorName { get; set; }

        public virtual Template Template { get; set; }
        public virtual ICollection<ObjectElementLink> ElementLinks { get; set; }
    }
}
