﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.S3;
using Amazon.S3.Model;

using Newtonsoft.Json;

using NuClear.VStore.Descriptors;
using NuClear.VStore.Descriptors.Templates;
using NuClear.VStore.Json;
using NuClear.VStore.Options;
using NuClear.VStore.S3;

namespace NuClear.VStore.Templates
{
    public sealed class TemplateStorageReader
    {
        private readonly IAmazonS3 _amazonS3;
        private readonly string _bucketName;

        public TemplateStorageReader(CephOptions cephOptions, IAmazonS3 amazonS3)
        {
            _amazonS3 = amazonS3;
            _bucketName = cephOptions.TemplatesBucketName;
        }

        public async Task<IReadOnlyCollection<ImmutableDescriptor>> GetTemplateMetadatas()
        {
            var listVersionsResponse = await _amazonS3.ListVersionsAsync(_bucketName);

            var descriptors = new ConcurrentBag<ImmutableDescriptor>();
            Parallel.ForEach(
                listVersionsResponse.Versions.FindAll(x => x.IsLatest && !x.IsDeleteMarker),
                obj => descriptors.Add(new ImmutableDescriptor(obj.Key, obj.VersionId, obj.LastModified)));

            return descriptors.OrderBy(x => x.LastModified).ToArray();
        }

        public async Task<TemplateDescriptor> GetTemplateDescriptor(long id, string versionId)
        {
            var objectVersionId = string.IsNullOrEmpty(versionId) ? await GetTemplateLatestVersion(id) : versionId;

            var response = await _amazonS3.GetObjectAsync(_bucketName, id.ToString(), objectVersionId);
            if (response.ResponseStream == null)
            {
                throw new ObjectNotFoundException($"Template '{id}' not found");
            }

            string json;
            using (var reader = new StreamReader(response.ResponseStream, Encoding.UTF8))
            {
                json = reader.ReadToEnd();
            }

            var descriptor = new TemplateDescriptor { Id = id, VersionId = objectVersionId, LastModified = response.LastModified };
            JsonConvert.PopulateObject(json, descriptor, SerializerSettings.Default);

            return descriptor;
        }

        public async Task<string> GetTemplateLatestVersion(long id)
        {
            var versionsResponse = await _amazonS3.ListVersionsAsync(_bucketName, id.ToString());
            return versionsResponse.Versions.Find(x => x.IsLatest).VersionId;
        }

        public async Task<bool> IsTemplateExists(long id)
        {
            var listResponse = await _amazonS3.ListObjectsV2Async(
                                   new ListObjectsV2Request
                                       {
                                           BucketName = _bucketName,
                                           Prefix = id.ToString()
                                       });
            return listResponse.S3Objects.Count != 0;
        }
    }
}