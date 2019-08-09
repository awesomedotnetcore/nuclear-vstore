namespace NuClear.VStore.Models
{
    public class ObjectElementLink
    {
        public long ObjectId { get; set; }
        public string ObjectVersionId { get; set; }
        public long ElementId { get; set; }
        public string ElementVersionId { get; set; }

        public virtual Object Object { get; set; }
        public virtual ObjectElement Element { get; set; }
    }
}