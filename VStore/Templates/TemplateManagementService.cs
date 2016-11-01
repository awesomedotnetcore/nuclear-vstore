﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.S3;
using Amazon.S3.Model;

using Newtonsoft.Json;

using NuClear.VStore.Descriptors;
using NuClear.VStore.Descriptors.Templates;
using NuClear.VStore.Json;
using NuClear.VStore.Locks;
using NuClear.VStore.Options;
using NuClear.VStore.S3;

namespace NuClear.VStore.Templates
{
    public sealed class TemplateManagementService
    {
        private static readonly IReadOnlyCollection<FileFormat> ImageFileFormats =
            new[] { FileFormat.Bmp, FileFormat.Gif, FileFormat.Png };

        private static readonly IReadOnlyCollection<FileFormat> ArticleFileFormats =
            new[] { FileFormat.Chm };

        private readonly IAmazonS3 _amazonS3;
        private readonly TemplateStorageReader _templateStorageReader;
        private readonly LockSessionFactory _lockSessionFactory;
        private readonly string _bucketName;

        public TemplateManagementService(
            CephOptions cephOptions,
            IAmazonS3 amazonS3,
            TemplateStorageReader templateStorageReader,
            LockSessionFactory lockSessionFactory)
        {
            _amazonS3 = amazonS3;
            _templateStorageReader = templateStorageReader;
            _lockSessionFactory = lockSessionFactory;
            _bucketName = cephOptions.TemplatesBucketName;
        }

        public IReadOnlyCollection<IElementDescriptor> GetAvailableElementDescriptors()
        {
            return new IElementDescriptor[]
                {
                    new TextElementDescriptor(),
                    new ImageElementDescriptor(),
                    new ArticleElementDescriptor(),
                    new FasCommantElementDescriptor(),
                    new DateElementDescriptor(),
                    new LinkElementDescriptor()
                };
        }

        public async Task<string> CreateTemplate(long id, ITemplateDescriptor templateDescriptor)
        {
            if (id == 0)
            {
                throw new ArgumentException($"Template Id must be set", nameof(id));
            }

            using (_lockSessionFactory.CreateLockSession(id))
            {
                if (await _templateStorageReader.IsTemplateExists(id))
                {
                    throw new InvalidOperationException($"Template '{id}' already exists");
                }

                await PutTemplate(id, templateDescriptor);

                // ceph does not return version-id response header, so we need to do another request to get version
                return await _templateStorageReader.GetTemplateLatestVersion(id);
            }
        }

        public async Task<string> ModifyTemplate(long id, string versionId, ITemplateDescriptor templateDescriptor)
        {
            if (id == 0)
            {
                throw new ArgumentException("Template Id must be set", nameof(id));
            }

            if (string.IsNullOrEmpty(versionId))
            {
                throw new ArgumentException("VersionId must be set", nameof(versionId));
            }

            using (_lockSessionFactory.CreateLockSession(id))
            {
                if (!await _templateStorageReader.IsTemplateExists(id))
                {
                    throw new InvalidOperationException($"Template '{id}' does not exist");
                }

                var latestVersionId = await _templateStorageReader.GetTemplateLatestVersion(id);
                if (!versionId.Equals(latestVersionId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Template '{id}' cannot be modified. " +
                                                        $"Reason: version '{versionId}' has been overwritten. " +
                                                        $"Latest versionId is '{latestVersionId}'");
                }

                await PutTemplate(id, templateDescriptor);

                // ceph does not return version-id response header, so we need to do another request to get version
                return await _templateStorageReader.GetTemplateLatestVersion(id);
            }
        }

        public void VerifyElementDescriptorsConsistency(long? templateId, IEnumerable<IElementDescriptor> elementDescriptors)
        {
            Parallel.ForEach(
                elementDescriptors,
                elementDescriptor =>
                    {
                        TextElementDescriptor textElementDescriptor;
                        ImageElementDescriptor imageElementDescriptor;
                        ArticleElementDescriptor articleElementDescriptor;
                        if ((textElementDescriptor = elementDescriptor as TextElementDescriptor) != null)
                        {
                            if (textElementDescriptor.MaxSymbols < textElementDescriptor.MaxSymbolsPerWord)
                            {
                                throw new TemplateInconsistentException(
                                          templateId,
                                          $"MaxSymbols must be equal or greater than MaxSymbolsPerWord");
                            }
                        }
                        else if ((imageElementDescriptor = elementDescriptor as ImageElementDescriptor) != null)
                        {
                            if (imageElementDescriptor.SupportedFileFormats.Any(x => !ImageFileFormats.Contains(x)))
                            {
                                throw new TemplateInconsistentException(
                                          templateId,
                                          $"Supported file formats for images are: {string.Join(",", ImageFileFormats)}");
                            }

                            if (imageElementDescriptor.ImageSize == ImageSize.Empty)
                            {
                                throw new TemplateInconsistentException(
                                          templateId,
                                          $"Image size must be set to the value different than: {ImageSize.Empty}");
                            }
                        }
                        else if ((articleElementDescriptor = elementDescriptor as ArticleElementDescriptor) != null)
                        {
                            if (articleElementDescriptor.SupportedFileFormats.Any(x => !ArticleFileFormats.Contains(x)))
                            {
                                throw new TemplateInconsistentException(
                                          templateId,
                                          $"Supported file formats for articles are: {string.Join(",", ImageFileFormats)}");
                            }
                        }
                    });
        }

        private async Task PutTemplate(long id, ITemplateDescriptor templateDescriptor)
        {
            VerifyElementDescriptorsConsistency(id, templateDescriptor.Elements);

            var putRequest = new PutObjectRequest
                {
                    Key = id.ToString(),
                    BucketName = _bucketName,
                    ContentType = ContentType.Json,
                    ContentBody = JsonConvert.SerializeObject(templateDescriptor, SerializerSettings.Default),
                    CannedACL = S3CannedACL.PublicRead,
                };
            await _amazonS3.PutObjectAsync(putRequest);
        }
    }
}