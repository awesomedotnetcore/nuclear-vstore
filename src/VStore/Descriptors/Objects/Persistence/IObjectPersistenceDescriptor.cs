using Newtonsoft.Json.Linq;

namespace NuClear.VStore.Descriptors.Objects.Persistence
{
    public interface IObjectPersistenceDescriptor : IDescriptor
    {
        Language Language { get; }
        JObject Properties { get; set; }
    }
}
