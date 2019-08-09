namespace NuClear.VStore.Descriptors.Sessions
{
    public sealed class BinaryMetadata
    {
        public BinaryMetadata(string filename, long fileSize, string contentType)
        {
            Filename = filename;
            FileSize = fileSize;
            ContentType = contentType;
        }

        public string Filename { get; }
        public long FileSize { get; }
        public string ContentType { get; set; }
    }
}