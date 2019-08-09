using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;

using NuClear.VStore.DataContract;
using NuClear.VStore.Descriptors;
using NuClear.VStore.Descriptors.Objects;
using NuClear.VStore.Descriptors.Objects.Persistence;
using NuClear.VStore.Descriptors.Templates;
using NuClear.VStore.Json;
using NuClear.VStore.Locks;
using NuClear.VStore.Models;
using NuClear.VStore.Options;
using NuClear.VStore.S3;
using NuClear.VStore.Templates;

using Object = NuClear.VStore.Models.Object;

namespace NuClear.VStore.Objects
{
    public sealed class ObjectsStorageReader : IObjectsStorageReader
    {
        private const int BatchCount = 1000;

        private readonly VStoreContext _context;
        private readonly CdnOptions _cdnOptions;
        private readonly ITemplatesStorageReader _templatesStorageReader;
        private readonly DistributedLockManager _distributedLockManager;

        public ObjectsStorageReader(
            VStoreContext context,
            CdnOptions cdnOptions,
            ITemplatesStorageReader templatesStorageReader,
            DistributedLockManager distributedLockManager)
        {
            _context = context;
            _cdnOptions = cdnOptions;
            _templatesStorageReader = templatesStorageReader;
            _distributedLockManager = distributedLockManager;
        }

        public async Task<ContinuationContainer<IdentifyableObjectRecord<long>>> List(string continuationToken)
        {
            var listResponse = _context.Objects.AsNoTracking();
            if (long.TryParse(continuationToken, out var parsedContinuationToken))
            {
                listResponse = listResponse.Where(x => x.Id > parsedContinuationToken);
            }

            IReadOnlyList<long> identifiers;
            IReadOnlyDictionary<long, DateTime> lastVersions;
            using (await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
            {
                identifiers = await listResponse.Select(x => x.Id)
                                                .Distinct()
                                                .OrderBy(x => x)
                                                .Take(BatchCount)
                                                .ToListAsync();

                var versions = await _context.Objects
                                             .AsNoTracking()
                                             .Where(x => identifiers.Contains(x.Id))
                                             .Select(x => new { x.Id, x.VersionIndex, x.LastModified })
                                             .ToListAsync();

                lastVersions = versions.GroupBy(x => x.Id, (id, g) => g.OrderByDescending(x => x.VersionIndex).First())
                                       .ToDictionary(x => x.Id, x => DateTime.SpecifyKind(x.LastModified, DateTimeKind.Utc));
            }

            var nextMarker = identifiers.Count > 0 ? identifiers[identifiers.Count - 1].ToString() : continuationToken;
            var records = identifiers.Select(x => new IdentifyableObjectRecord<long>(x, lastVersions[x]))
                                     .ToList();

            return new ContinuationContainer<IdentifyableObjectRecord<long>>(records, nextMarker);
        }

        public async Task<IReadOnlyCollection<ObjectMetadataRecord>> GetObjectsMetadata(IReadOnlyCollection<long> ids)
        {
            var uniqueIds = new HashSet<long>(ids);
            var versions = await _context.Objects
                                         .AsNoTracking()
                                         .Where(x => uniqueIds.Contains(x.Id))
                                         .ToListAsync();

            var lastVersions = versions.GroupBy(x => x.Id, (id, g) => g.OrderByDescending(x => x.VersionIndex).First());

            var result = lastVersions.Select(x => new ObjectMetadataRecord(
                                                 x.Id,
                                                 x.VersionId,
                                                 DateTime.SpecifyKind(x.LastModified, DateTimeKind.Utc),
                                                 new AuthorInfo(x.Author, x.AuthorLogin, x.AuthorName)))
                                     .ToList();

            return result;
        }

        public async Task<IVersionedTemplateDescriptor> GetTemplateDescriptor(long id, string versionId)
        {
            var entity = await _context.Objects
                                       .AsNoTracking()
                                       .Where(x => x.Id == id && x.VersionId == versionId)
                                       .Select(x => new { x.TemplateId, x.TemplateVersionId })
                                       .FirstOrDefaultAsync();

            if (entity == null)
            {
                throw new ObjectNotFoundException($"Object '{id}' with versionId '{versionId}' not found.");
            }

            return await _templatesStorageReader.GetTemplateDescriptor(entity.TemplateId, entity.TemplateVersionId);
        }

        public async Task<IReadOnlyCollection<ObjectVersionRecord>> GetObjectVersions(long id, string initialVersionId) =>
            await GetObjectVersions(id, initialVersionId, true);

        public async Task<IReadOnlyCollection<ObjectVersionMetadataRecord>> GetObjectVersionsMetadata(long id, string initialVersionId) =>
            await GetObjectVersions(id, initialVersionId, false);

        /// <inheritdoc />
        public async Task<VersionedObjectDescriptor<long>> GetObjectLatestVersion(long id)
            => await _context.Objects
                             .AsNoTracking()
                             .Where(x => x.Id == id)
                             .OrderByDescending(x => x.VersionIndex)
                             .Select(x => new VersionedObjectDescriptor<long>(x.Id, x.VersionId, DateTime.SpecifyKind(x.LastModified, DateTimeKind.Utc)))
                             .FirstOrDefaultAsync();

        public async Task<ObjectDescriptor> GetObjectDescriptor(long id, string versionId, CancellationToken cancellationToken) =>
            await GetObjectDescriptor(id, versionId, cancellationToken, true);

        public async Task<bool> IsObjectExists(long id)
            => await _context.Objects
                             .AsNoTracking()
                             .AnyAsync(x => x.Id == id);

        /// <inheritdoc />
        public async Task<DateTime> GetObjectVersionLastModified(long id, string versionId)
        {
            var entity = await _context.Objects
                                       .AsNoTracking()
                                       .Where(x => x.Id == id && x.VersionId == versionId)
                                       .Select(x => new { x.Id, x.VersionId, x.LastModified })
                                       .FirstOrDefaultAsync();

            if (entity == null)
            {
                throw new ObjectNotFoundException($"Object '{id}' with versionId '{versionId}' not found.");
            }

            return DateTime.SpecifyKind(entity.LastModified, DateTimeKind.Utc);
        }

        /// <inheritdoc/>
        public async Task<IImageElementValue> GetImageElementValue(long id, string versionId, int templateCode)
        {
            var objectDescriptor = await GetObjectDescriptor(id, versionId, CancellationToken.None);
            var element = objectDescriptor.Elements.SingleOrDefault(x => x.TemplateCode == templateCode);
            if (element == null)
            {
                throw new ObjectNotFoundException($"Element with template code '{templateCode}' of object/versionId '{id}/{versionId}' not found.");
            }

            if (!(element.Value is IImageElementValue elementValue))
            {
                throw new ObjectElementInvalidTypeException(
                    $"Element with template code '{templateCode}' of object/versionId '{id}/{versionId}' is not an image.");
            }

            return elementValue;
        }

        private async Task<IReadOnlyCollection<ObjectVersionRecord>> GetObjectVersions(long id, string initialVersionId, bool fetchElements)
        {
            await _distributedLockManager.EnsureLockNotExistsAsync(id);
            var query = _context.Objects
                                .AsNoTracking()
                                .Where(x => x.Id == id)
                                .OrderByDescending(x => x.VersionIndex)
                                .AsQueryable();

            if (fetchElements)
            {
                query = query.Include(x => x.ElementLinks)
                             .ThenInclude(x => x.Element);
            }

            var allVersions = await query.ToListAsync();

            if (allVersions.Count == 0)
            {
                throw new ObjectNotFoundException($"Object '{id}' not found.");
            }

            var versions = allVersions.TakeWhile(x => x.VersionId != initialVersionId);
            var records = new List<ObjectVersionRecord>(allVersions.Count);
            foreach (var version in versions)
            {
                var elements = fetchElements ? version.ElementLinks.Select(x => x.Element) : Array.Empty<ObjectElement>();
                var descriptor = GetObjectDescriptorFromEntity(version, elements);
                records.Add(new ObjectVersionRecord(
                    version.Id,
                    version.VersionId,
                    version.VersionIndex,
                    version.TemplateId,
                    version.TemplateVersionId,
                    DateTime.SpecifyKind(version.LastModified, DateTimeKind.Utc),
                    new AuthorInfo(version.Author, version.AuthorLogin, version.AuthorName),
                    descriptor.Properties,
                    descriptor.Elements
                              .Select(x => new ObjectVersionRecord.ElementRecord(x.TemplateCode, x.Value))
                              .ToList(),
                    version.ModifiedElements));
            }

            return records;
        }

        private ObjectDescriptor GetObjectDescriptorFromEntity(Object entity, IEnumerable<ObjectElement> elements, bool setCdnUris = false)
        {
            var persistenceDescriptor = JsonConvert.DeserializeObject<ObjectPersistenceDescriptor>(entity.Data, SerializerSettings.Default);
            var descriptor = new ObjectDescriptor
                {
                    Id = entity.Id,
                    VersionId = entity.VersionId,
                    VersionIndex = entity.VersionIndex,
                    LastModified = DateTime.SpecifyKind(entity.LastModified, DateTimeKind.Utc),
                    TemplateId = entity.TemplateId,
                    TemplateVersionId = entity.TemplateVersionId,
                    Language = persistenceDescriptor.Language,
                    Properties = persistenceDescriptor.Properties,
                    Elements = elements.Select(x => GetObjectElementDescriptorFromEntity(entity.Id, entity.VersionId, x, setCdnUris)).ToList(),
                    Metadata = new ObjectDescriptor.ObjectMetadata
                        {
                            Author = entity.Author,
                            AuthorLogin = entity.AuthorLogin,
                            AuthorName = entity.AuthorName,
                            ModifiedElements = entity.ModifiedElements
                        }
                };

            return descriptor;
        }

        private ObjectElementDescriptor GetObjectElementDescriptorFromEntity(long objectId, string objectVersionId, ObjectElement entity, bool setCdnUris)
        {
            var descriptor = JsonConvert.DeserializeObject<ObjectElementPersistenceDescriptor>(entity.Data, SerializerSettings.Default);
            if (setCdnUris)
            {
                SetCdnUris(objectId, objectVersionId, descriptor);
            }

            return new ObjectElementDescriptor
                {
                    Id = entity.Id,
                    VersionId = entity.VersionId,
                    LastModified = DateTime.SpecifyKind(entity.LastModified, DateTimeKind.Utc),
                    Constraints = descriptor.Constraints,
                    Properties = descriptor.Properties,
                    TemplateCode = descriptor.TemplateCode,
                    Type = descriptor.Type,
                    Value = descriptor.Value
                };
        }

        private async Task<ObjectDescriptor> GetObjectDescriptor(long id, string versionId, CancellationToken cancellationToken, bool fetchElements)
        {
            var query = _context.Objects
                                .AsNoTracking()
                                .Where(x => x.Id == id);

            query = string.IsNullOrEmpty(versionId)
                        ? query.OrderByDescending(x => x.VersionIndex)
                        : query.Where(x => x.VersionId == versionId);

            if (fetchElements)
            {
                query = query.Include(x => x.ElementLinks)
                             .ThenInclude(x => x.Element);
            }

            var entity = await query.FirstOrDefaultAsync(cancellationToken);
            if (entity == null)
            {
                throw new ObjectNotFoundException($"Object '{id}' not found.");
            }

            var elements = fetchElements ? entity.ElementLinks.Select(x => x.Element) : Array.Empty<ObjectElement>();
            var descriptor = GetObjectDescriptorFromEntity(entity, elements, true);

            return descriptor;
        }

        private void SetCdnUris(long id, string versionId, IObjectElementPersistenceDescriptor objectElementDescriptor)
        {
            if (!(objectElementDescriptor.Value is IBinaryElementValue binaryElementValue) || string.IsNullOrEmpty(binaryElementValue.Raw))
            {
                return;
            }

            binaryElementValue.DownloadUri = _cdnOptions.AsRawUri(binaryElementValue.Raw);

            switch (binaryElementValue)
            {
                case ICompositeBitmapImageElementValue compositeBitmapImageElementValue:
                    compositeBitmapImageElementValue.PreviewUri = _cdnOptions.AsCompositePreviewUri(id, versionId, objectElementDescriptor.TemplateCode);
                    foreach (var image in compositeBitmapImageElementValue.SizeSpecificImages)
                    {
                        image.DownloadUri = _cdnOptions.AsRawUri(image.Raw);
                    }

                    break;
                case IScalableBitmapImageElementValue scalableBitmapImageElementValue:
                    scalableBitmapImageElementValue.PreviewUri = _cdnOptions.AsScalablePreviewUri(id, versionId, objectElementDescriptor.TemplateCode);
                    break;
                case IImageElementValue imageElementValue:
                    imageElementValue.PreviewUri = _cdnOptions.AsRawUri(imageElementValue.Raw);
                    break;
            }
        }
    }
}
