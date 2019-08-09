using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using Newtonsoft.Json;

using NuClear.VStore.DataContract;
using NuClear.VStore.Descriptors.Templates;
using NuClear.VStore.Json;
using NuClear.VStore.Models;
using NuClear.VStore.S3;

namespace NuClear.VStore.Templates
{
    public sealed class TemplatesStorageReader : ITemplatesStorageReader
    {
        private const int BatchCount = 1000;

        private readonly VStoreContext _context;
        private readonly IMemoryCache _memoryCache;

        public TemplatesStorageReader(VStoreContext context, IMemoryCache memoryCache)
        {
            _context = context;
            _memoryCache = memoryCache;
        }

        public async Task<ContinuationContainer<IdentifyableObjectRecord<long>>> List(string continuationToken)
        {
            var listResponse = _context.Templates.AsNoTracking();
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

                var versions = await _context.Templates
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

        public async Task<IReadOnlyCollection<ObjectMetadataRecord>> GetTemplatesMetadata(IReadOnlyCollection<long> ids)
        {
            var uniqueIds = new HashSet<long>(ids);
            var versions = await _context.Templates
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

        public async Task<TemplateDescriptor> GetTemplateDescriptor(long id, string versionId)
        {
            var templateVersionId = string.IsNullOrEmpty(versionId) ? (await GetTemplateLatestVersion(id)).versionId : versionId;

            var templateDescriptor = await _memoryCache.GetOrCreateAsync(
                id.AsCacheEntryKey(templateVersionId),
                async entry =>
                    {
                        var template = await _context.Templates
                                                     .AsNoTracking()
                                                     .Where(x => x.Id == id && x.VersionId == templateVersionId)
                                                     .FirstOrDefaultAsync();

                        if (template == null)
                        {
                            throw new ObjectNotFoundException($"Template '{id}' version '{templateVersionId}' not found");
                        }

                        var descriptor = GetTemplateDescriptorFromEntity(template);

                        entry.SetValue(descriptor)
                             .SetPriority(CacheItemPriority.NeverRemove);

                        return descriptor;
                    });

            return templateDescriptor;
        }

        public async Task<(string versionId, int versionIndex)> GetTemplateLatestVersion(long id)
        {
            var templateVersion = await _context.Templates
                                                .AsNoTracking()
                                                .Where(x => x.Id == id)
                                                .OrderByDescending(x => x.VersionIndex)
                                                .FirstOrDefaultAsync();

            if (templateVersion == null)
            {
                throw new ObjectNotFoundException($"Template '{id}' versions not found");
            }

            return (templateVersion.VersionId, templateVersion.VersionIndex);
        }

        public async Task<bool> IsTemplateExists(long id) =>
            await _context.Templates
                          .AsNoTracking()
                          .AnyAsync(x => x.Id == id);

        public async Task<IReadOnlyCollection<TemplateVersionRecord>> GetTemplateVersions(long id)
        {
            var templateVersions = await _context.Templates
                                                 .AsNoTracking()
                                                 .Where(x => x.Id == id)
                                                 .OrderByDescending(x => x.VersionIndex)
                                                 .ToListAsync();

            if (templateVersions.Count == 0)
            {
                throw new ObjectNotFoundException($"Template '{id}' not found.");
            }

            var records = new List<TemplateVersionRecord>(templateVersions.Count);
            foreach (var template in templateVersions)
            {
                var descriptor = GetTemplateDescriptorFromEntity(template);
                records.Add(new TemplateVersionRecord(
                                template.Id,
                                template.VersionId,
                                template.VersionIndex,
                                DateTime.SpecifyKind(template.LastModified, DateTimeKind.Utc),
                                new AuthorInfo(template.Author, template.AuthorLogin, template.AuthorName),
                                descriptor.Properties,
                                descriptor.Elements.Select(x => x.TemplateCode).ToList()));
            }

            return records;
        }

        private static TemplateDescriptor GetTemplateDescriptorFromEntity(Template template)
        {
            var descriptor = new TemplateDescriptor
                {
                    Id = template.Id,
                    VersionId = template.VersionId,
                    Author = template.Author,
                    AuthorLogin = template.AuthorLogin,
                    AuthorName = template.AuthorName,
                    LastModified = DateTime.SpecifyKind(template.LastModified, DateTimeKind.Utc)
                };

            JsonConvert.PopulateObject(template.Data, descriptor, SerializerSettings.Default);
            return descriptor;
        }
    }
}
