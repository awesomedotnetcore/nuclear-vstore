using System.Collections.Generic;

using NuClear.VStore.Descriptors.Objects.Persistence;

namespace NuClear.VStore.Descriptors.Objects
{
    public interface IObjectDescriptor : IObjectPersistenceDescriptor
    {
        long TemplateId { get; set; }
        string TemplateVersionId { get; set; }
        IReadOnlyCollection<IObjectElementDescriptor> Elements { get; set; }
    }
}