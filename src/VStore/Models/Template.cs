using System;
using System.Collections.Generic;

namespace NuClear.VStore.Models
{
    public class Template
    {
        public Template()
        {
            Objects = new HashSet<Object>();
        }

        public long Id { get; set; }
        public string VersionId { get; set; }
        public int VersionIndex { get; set; }
        public DateTime LastModified { get; set; }
        public string Author { get; set; }
        public string AuthorLogin { get; set; }
        public string AuthorName { get; set; }
        public string Data { get; set; }

        public virtual ICollection<Object> Objects { get; set; }
    }
}
