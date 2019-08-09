using Newtonsoft.Json.Linq;

namespace NuClear.VStore.Descriptors.Objects.Persistence
{
    public sealed class ObjectPersistenceDescriptor : IObjectPersistenceDescriptor
    {
        public Language Language { get; set; }
        public JObject Properties { get; set; }
    }
}