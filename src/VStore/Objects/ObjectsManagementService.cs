using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using NuClear.VStore.DataContract;
using NuClear.VStore.Descriptors;
using NuClear.VStore.Descriptors.Objects;
using NuClear.VStore.Descriptors.Objects.Persistence;
using NuClear.VStore.Descriptors.Sessions;
using NuClear.VStore.Descriptors.Templates;
using NuClear.VStore.Events;
using NuClear.VStore.Json;
using NuClear.VStore.Kafka;
using NuClear.VStore.Locks;
using NuClear.VStore.Models;
using NuClear.VStore.Objects.ContentPreprocessing;
using NuClear.VStore.Objects.ContentValidation;
using NuClear.VStore.Objects.ContentValidation.Errors;
using NuClear.VStore.Options;
using NuClear.VStore.Prometheus;
using NuClear.VStore.S3;
using NuClear.VStore.Sessions;
using NuClear.VStore.Sessions.ContentValidation.Errors;
using NuClear.VStore.Sessions.Upload;
using NuClear.VStore.Templates;
using Prometheus;

using Object = NuClear.VStore.Models.Object;

namespace NuClear.VStore.Objects
{
    public sealed class ObjectsManagementService : IObjectsManagementService
    {
        private readonly ILogger<ObjectsManagementService> _logger;
        private readonly VStoreContext _context;
        private readonly ITemplatesStorageReader _templatesStorageReader;
        private readonly IObjectsStorageReader _objectsStorageReader;
        private readonly SessionStorageReader _sessionStorageReader;
        private readonly DistributedLockManager _distributedLockManager;
        private readonly IEventSender _eventSender;
        private readonly string _objectEventsTopic;
        private readonly Counter _referencedBinariesMetric;

        public ObjectsManagementService(
            ILogger<ObjectsManagementService> logger,
            KafkaOptions kafkaOptions,
            VStoreContext context,
            ITemplatesStorageReader templatesStorageReader,
            IObjectsStorageReader objectsStorageReader,
            SessionStorageReader sessionStorageReader,
            DistributedLockManager distributedLockManager,
            IEventSender eventSender,
            MetricsProvider metricsProvider)
        {
            _logger = logger;
            _context = context;
            _templatesStorageReader = templatesStorageReader;
            _objectsStorageReader = objectsStorageReader;
            _sessionStorageReader = sessionStorageReader;
            _distributedLockManager = distributedLockManager;
            _eventSender = eventSender;
            _objectEventsTopic = kafkaOptions.ObjectEventsTopic;
            _referencedBinariesMetric = metricsProvider.GetReferencedBinariesMetric();
        }

        private delegate IEnumerable<ObjectElementValidationError> ValidationRule(IObjectElementValue value, IElementConstraints constraints);

        public async Task<string> Create(long id, AuthorInfo authorInfo, IObjectDescriptor objectDescriptor)
        {
            CheckRequiredProperties(id, objectDescriptor);

            using (await _distributedLockManager.AcquireLockAsync(id))
            {
                if (await _objectsStorageReader.IsObjectExists(id))
                {
                    throw new ObjectAlreadyExistsException(id);
                }

                var templateDescriptor = await _templatesStorageReader.GetTemplateDescriptor(objectDescriptor.TemplateId, objectDescriptor.TemplateVersionId);
                if (templateDescriptor.Elements.Count != objectDescriptor.Elements.Count)
                {
                    throw new ObjectInconsistentException(
                        id,
                        $"Quantity of elements in the object doesn't match to the quantity of elements in the corresponding template with Id '{objectDescriptor.TemplateId}' and versionId '{objectDescriptor.TemplateVersionId}'.");
                }

                var elementIds = new HashSet<long>(objectDescriptor.Elements.Select(x => x.Id));
                if (elementIds.Count != objectDescriptor.Elements.Count)
                {
                    throw new ObjectInconsistentException(id, "Some elements have non-unique identifiers.");
                }

                EnsureObjectElementsState(id, templateDescriptor.Elements, objectDescriptor.Elements);

                return await PutObject(id, null, null, authorInfo, Enumerable.Empty<IObjectElementDescriptor>(), objectDescriptor);
            }
        }

        public async Task<string> Modify(long id, string versionId, AuthorInfo authorInfo, IObjectDescriptor modifiedObjectDescriptor)
        {
            CheckRequiredProperties(id, modifiedObjectDescriptor);

            if (string.IsNullOrEmpty(versionId))
            {
                throw new InputDataValidationException("Object version must be set");
            }

            using (await _distributedLockManager.AcquireLockAsync(id))
            {
                var objectDescriptor = await _objectsStorageReader.GetObjectDescriptor(id, null, CancellationToken.None);
                if (!versionId.Equals(objectDescriptor.VersionId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConcurrencyException(id, versionId, objectDescriptor.VersionId);
                }

                if (modifiedObjectDescriptor.TemplateId != objectDescriptor.TemplateId)
                {
                    throw new ObjectInconsistentException(
                        id,
                        $"Modified and latest objects templates do not match ({modifiedObjectDescriptor.TemplateId} and {objectDescriptor.TemplateId}).");
                }

                if (!string.Equals(modifiedObjectDescriptor.TemplateVersionId, objectDescriptor.TemplateVersionId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ObjectInconsistentException(
                        id,
                        $"Modified and latest objects template versions do not match ({modifiedObjectDescriptor.TemplateVersionId} and {objectDescriptor.TemplateVersionId}).");
                }

                if (modifiedObjectDescriptor.Language != objectDescriptor.Language)
                {
                    throw new ObjectInconsistentException(
                        id,
                        $"Modified and latest objects languages do not match ({modifiedObjectDescriptor.Language} and {objectDescriptor.Language}).");
                }

                var modifiedElementsIds = new HashSet<long>(modifiedObjectDescriptor.Elements.Select(x => x.Id));
                if (modifiedElementsIds.Count != modifiedObjectDescriptor.Elements.Count)
                {
                    throw new ObjectInconsistentException(id, "Some elements have non-unique identifiers.");
                }

                var currentElementsIds = new HashSet<long>(objectDescriptor.Elements.Select(x => x.Id));
                if (!modifiedElementsIds.IsSubsetOf(currentElementsIds))
                {
                    throw new ObjectInconsistentException(id, "Modified object contains non-existing elements.");
                }

                EnsureObjectElementsState(id, objectDescriptor.Elements, modifiedObjectDescriptor.Elements);

                return await PutObject(id, versionId, objectDescriptor.VersionIndex, authorInfo, objectDescriptor.Elements, modifiedObjectDescriptor, currentElementsIds);
            }
        }

        public async Task<string> Upgrade(
            long id,
            string versionId,
            AuthorInfo authorInfo,
            IReadOnlyCollection<int> modifiedElementsTemplateCodes,
            IObjectDescriptor upgradedObjectDescriptor)
        {
            CheckRequiredProperties(id, upgradedObjectDescriptor);

            if (string.IsNullOrEmpty(versionId))
            {
                throw new ObjectInconsistentException(id, "Object version must be set");
            }

            using (await _distributedLockManager.AcquireLockAsync(id))
            {
                var latestObjectDescriptor = await _objectsStorageReader.GetObjectDescriptor(id, null, CancellationToken.None);
                if (!versionId.Equals(latestObjectDescriptor.VersionId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConcurrencyException(id, versionId, latestObjectDescriptor.VersionId);
                }

                if (upgradedObjectDescriptor.TemplateId != latestObjectDescriptor.TemplateId)
                {
                    throw new ObjectUpgradeException(
                        id,
                        $"Upgraded and latest objects templates do not match ({upgradedObjectDescriptor.TemplateId} and {latestObjectDescriptor.TemplateId}).");
                }

                if (string.Equals(upgradedObjectDescriptor.TemplateVersionId, latestObjectDescriptor.TemplateVersionId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ObjectUpgradeException(id, "Upgraded and latest objects template versions do not differ.");
                }

                if (upgradedObjectDescriptor.Language != latestObjectDescriptor.Language)
                {
                    throw new ObjectUpgradeException(
                        id,
                        $"Upgraded and latest objects languages do not match ({upgradedObjectDescriptor.Language} and {latestObjectDescriptor.Language}).");
                }

                var upgradedObjectElementIds = new HashSet<long>(upgradedObjectDescriptor.Elements.Select(x => x.Id));
                if (upgradedObjectElementIds.Count != upgradedObjectDescriptor.Elements.Count)
                {
                    throw new ObjectInconsistentException(id, "Some elements have non-unique identifiers.");
                }

                var upgradedObjectTemplateDescriptor = await _templatesStorageReader.GetTemplateDescriptor(upgradedObjectDescriptor.TemplateId, upgradedObjectDescriptor.TemplateVersionId);
                if (upgradedObjectTemplateDescriptor.Elements.Count != upgradedObjectDescriptor.Elements.Count)
                {
                    throw new ObjectInconsistentException(
                        id,
                        $"Quantity of elements in the object doesn't match to the quantity of elements in the corresponding template with Id '{upgradedObjectTemplateDescriptor.Id}' and versionId '{upgradedObjectTemplateDescriptor.VersionId}'.");
                }

                await EnsureTemplateVersionIsNewer(upgradedObjectTemplateDescriptor.Id, upgradedObjectTemplateDescriptor.VersionId, latestObjectDescriptor.TemplateVersionId);

                EnsureObjectElementsState(id, upgradedObjectTemplateDescriptor.Elements, upgradedObjectDescriptor.Elements);

                return await PutObject(
                           id,
                           versionId,
                           latestObjectDescriptor.VersionIndex,
                           authorInfo,
                           latestObjectDescriptor.Elements,
                           upgradedObjectDescriptor,
                           upgradedObjectElementIds,
                           modifiedElementsTemplateCodes);
            }
        }

        private async Task EnsureTemplateVersionIsNewer(long id, string versionIdToBeNewer, string versionIdToBeOlder)
        {
            var templateVersions = await _templatesStorageReader.GetTemplateVersions(id);
            var newerVersion = templateVersions.FirstOrDefault(x => x.VersionId == versionIdToBeNewer);
            var olderVersion = templateVersions.FirstOrDefault(x => x.VersionId == versionIdToBeOlder);
            if (newerVersion == null)
            {
                _logger.LogCritical("Template {id} doesn't have version {version}", id, versionIdToBeNewer);
                throw new InvalidOperationException($"Template {id} doesn't have version {versionIdToBeNewer}");
            }

            if (olderVersion == null)
            {
                _logger.LogCritical("Template {id} doesn't have version {version}", id, versionIdToBeOlder);
                throw new InvalidOperationException($"Template {id} doesn't have version {versionIdToBeOlder}");
            }

            if (newerVersion.VersionIndex <= olderVersion.VersionIndex)
            {
                throw new ObjectUpgradeException(
                    id,
                    $"Upgraded object template {id} version must be newer than latest object template version " +
                    $"('{newerVersion.VersionId}' with index {newerVersion.VersionIndex} and '{olderVersion.VersionId}' with index {olderVersion.VersionIndex} respectively).");
            }
        }

        private async Task<IReadOnlyDictionary<int, IReadOnlyCollection<BinaryValidationError>>> VerifyObjectBinaryElementsConsistency(
                Language language,
                IReadOnlyCollection<IObjectElementDescriptor> elements)
        {
            var errors = new Dictionary<int, IReadOnlyCollection<BinaryValidationError>>();
            foreach (var binaryElement in elements.GetBinaryElements())
            {
                var elementErrors = new List<BinaryValidationError>();
                var constraints = binaryElement.Constraints.For(language);
                var fileKeys = binaryElement.Value.ExtractFileKeys();
                foreach (var fileKey in fileKeys)
                {
                    var (stream, metaData) = await _sessionStorageReader.GetBinaryData(fileKey);
                    using (stream)
                    {
                        IUploadedFileMetadata binaryMetadata;
                        if (binaryElement.Value is ICompositeBitmapImageElementValue compositeBitmapImageElementValue &&
                            compositeBitmapImageElementValue.Raw != fileKey)
                        {
                            var image = compositeBitmapImageElementValue.SizeSpecificImages.First(x => x.Raw == fileKey);
                            binaryMetadata = new UploadedImageMetadata(metaData.Filename, metaData.ContentType, metaData.FileSize, image.Size);
                        }
                        else
                        {
                            binaryMetadata = new GenericUploadedFileMetadata(metaData.Filename, metaData.ContentType, metaData.FileSize);
                        }

                        try
                        {
                            BinaryValidationUtils.EnsureFileMetadataIsValid(binaryElement, language, binaryMetadata);
                            BinaryValidationUtils.EnsureFileHeaderIsValid(binaryElement.TemplateCode, binaryElement.Type, constraints, stream, binaryMetadata);
                            BinaryValidationUtils.EnsureFileContentIsValid(binaryElement.TemplateCode, binaryElement.Type, constraints, stream, metaData.Filename);
                        }
                        catch (InvalidBinaryException e)
                        {
                            elementErrors.Add(e.Error);
                        }
                    }
                }

                if (elementErrors.Count > 0)
                {
                    errors[binaryElement.TemplateCode] = elementErrors;
                }
            }

            return errors;
        }

        private static void EnsureObjectElementsState(
            long objectId,
            IReadOnlyCollection<IElementDescriptor> referenceElementDescriptors,
            IEnumerable<IElementDescriptor> elementDescriptors)
        {
            var templateCodes = new HashSet<int>();
            foreach (var elementDescriptor in elementDescriptors)
            {
                var referenceObjectElement = referenceElementDescriptors.SingleOrDefault(x => x.TemplateCode == elementDescriptor.TemplateCode);
                if (referenceObjectElement == null)
                {
                    throw new ObjectInconsistentException(objectId, $"Element with template code '{elementDescriptor.TemplateCode}' not found in the template.");
                }

                if (!templateCodes.Add(elementDescriptor.TemplateCode))
                {
                    throw new ObjectInconsistentException(objectId, $"Element with template code '{elementDescriptor.TemplateCode}' must be unique within the object.");
                }

                if (referenceObjectElement.Type != elementDescriptor.Type)
                {
                    throw new ObjectInconsistentException(
                        objectId,
                        $"Type of the element with template code '{referenceObjectElement.TemplateCode}' ({referenceObjectElement.Type}) doesn't match to the type of corresponding element in template ({elementDescriptor.Type}).");
                }

                if (!referenceObjectElement.Constraints.Equals(elementDescriptor.Constraints))
                {
                    throw new ObjectInconsistentException(
                        objectId,
                        $"Constraints for the element with template code '{referenceObjectElement.TemplateCode}' doesn't match to constraints for corresponding element in template.");
                }
            }
        }

        private static void CheckRequiredProperties(long id, IObjectDescriptor objectDescriptor)
        {
            if (id <= 0)
            {
                throw new InputDataValidationException("Object Id must be set");
            }

            if (objectDescriptor.Language == Language.Unspecified)
            {
                throw new InputDataValidationException("Language must be specified");
            }

            if (objectDescriptor.TemplateId <= 0)
            {
                throw new InputDataValidationException("Template Id must be specified");
            }

            if (string.IsNullOrEmpty(objectDescriptor.TemplateVersionId))
            {
                throw new InputDataValidationException("Template versionId must be specified");
            }

            if (objectDescriptor.Properties == null)
            {
                throw new InputDataValidationException("Object properties must be specified");
            }

            if (objectDescriptor.Elements == null)
            {
                throw new InputDataValidationException("Object elements must be specified");
            }
        }

        private static async Task<IReadOnlyDictionary<int, IReadOnlyCollection<ObjectElementValidationError>>> VerifyObjectElementsConsistency(
            Language language,
            IEnumerable<IObjectElementDescriptor> elementDescriptors)
        {
            var allErrors = new ConcurrentDictionary<int, IReadOnlyCollection<ObjectElementValidationError>>();
            var tasks = elementDescriptors.Select(
                async element =>
                    await Task.Run(() =>
                                       {
                                           var errors = new List<ObjectElementValidationError>();
                                           var constraints = element.Constraints.For(language);
                                           var rules = GetValidationRules(element);

                                           foreach (var validationRule in rules)
                                           {
                                               errors.AddRange(validationRule(element.Value, constraints));
                                           }

                                           if (errors.Count > 0)
                                           {
                                               allErrors[element.TemplateCode] = errors;
                                           }
                                       }));

            await Task.WhenAll(tasks);

            return allErrors;
        }

        private static IEnumerable<ValidationRule> GetValidationRules(IObjectElementDescriptor descriptor)
        {
            switch (descriptor.Type)
            {
                case ElementDescriptorType.PlainText:
                case ElementDescriptorType.FasComment:
                    return new ValidationRule[]
                        {
                            PlainTextValidator.CheckLength,
                            PlainTextValidator.CheckWordsLength,
                            PlainTextValidator.CheckLinesCount,
                            PlainTextValidator.CheckRestrictedSymbols
                        };
                case ElementDescriptorType.FormattedText:
                    return new ValidationRule[]
                        {
                            FormattedTextValidator.CheckLength,
                            FormattedTextValidator.CheckWordsLength,
                            FormattedTextValidator.CheckLinesCount,
                            FormattedTextValidator.CheckRestrictedSymbols,
                            FormattedTextValidator.CheckValidHtml,
                            FormattedTextValidator.CheckSupportedHtmlTags,
                            FormattedTextValidator.CheckAttributesAbsence,
                            FormattedTextValidator.CheckEmptyList,
                            FormattedTextValidator.CheckNestedList,
                            FormattedTextValidator.CheckUnsupportedListElements
                        };
                case ElementDescriptorType.Link:
                case ElementDescriptorType.VideoLink:
                    return new ValidationRule[]
                        {
                            LinkValidator.CheckLink,
                            PlainTextValidator.CheckLength,
                            PlainTextValidator.CheckRestrictedSymbols
                        };
                case ElementDescriptorType.BitmapImage:
                case ElementDescriptorType.VectorImage:
                case ElementDescriptorType.ScalableBitmapImage:
                case ElementDescriptorType.Article:
                case ElementDescriptorType.Phone:
                    return new ValidationRule[] { };
                case ElementDescriptorType.Color:
                    return new ValidationRule[] { ColorValidator.CheckValidColor };
                case ElementDescriptorType.CompositeBitmapImage:
                    return new ValidationRule[] { CompositeBitmapImageValidator.CheckValidCompositeBitmapImage };
                default:
                    throw new ArgumentOutOfRangeException(nameof(descriptor.Type), descriptor.Type, $"Unsupported element descriptor type for descriptor {descriptor.Id}");
            }
        }

        private async Task<string> PutObject(
            long id,
            string oldVersionId,
            int? prevVersionIndex,
            AuthorInfo authorInfo,
            IEnumerable<IObjectElementDescriptor> currentObjectElements,
            IObjectDescriptor objectDescriptor,
            IReadOnlyCollection<long> objectElementIdsToSave = null,
            IEnumerable<int> modifiedElementsTemplateCodes = null)
        {
            PreprocessObjectElements(objectDescriptor.Elements);
            var nonBinaryErrors = await VerifyObjectElementsConsistency(objectDescriptor.Language, objectDescriptor.Elements);
            var (metadataForBinaries, binaryMetadataErrors) = await RetrieveMetadataForBinaries(currentObjectElements, objectDescriptor.Elements);
            if (binaryMetadataErrors.Count > 0)
            {
                var mergedErrors = nonBinaryErrors.Concat(binaryMetadataErrors)
                                                  .GroupBy(x => x.Key, x => x.Value)
                                                  .ToDictionary(x => x.Key, x => (IReadOnlyCollection<ObjectElementValidationError>)x.SelectMany(y => y).ToList());

                throw new InvalidObjectException(id, mergedErrors);
            }

            var binaryErrors = await VerifyObjectBinaryElementsConsistency(objectDescriptor.Language, objectDescriptor.Elements);
            if (nonBinaryErrors.Count > 0 || binaryErrors.Count > 0)
            {
                throw new InvalidObjectException(id, nonBinaryErrors, binaryErrors);
            }

            await _eventSender.SendAsync(_objectEventsTopic, new ObjectVersionCreatingEvent(id, oldVersionId));

            var totalBinariesCount = 0;
            var newElementsVersions = new List<ObjectElement>();
            foreach (var elementDescriptor in objectDescriptor.Elements)
            {
                var (elementPersistenceValue, binariesCount) = ConvertToPersistenceValue(elementDescriptor.Value, metadataForBinaries);
                var elementPersistenceDescriptor = new ObjectElementPersistenceDescriptor(elementDescriptor, elementPersistenceValue);
                totalBinariesCount += binariesCount;
                var data = JsonConvert.SerializeObject(elementPersistenceDescriptor, SerializerSettings.Default);
                var newElement = new ObjectElement
                    {
                        Id = elementDescriptor.Id,
                        Data = data,
                        LastModified = DateTime.UtcNow
                    };

                newElementsVersions.Add(newElement);
            }

            var objectPersistenceDescriptor = new ObjectPersistenceDescriptor
                {
                    Language = objectDescriptor.Language,
                    Properties = objectDescriptor.Properties
                };

            var modifiedElements = modifiedElementsTemplateCodes ?? objectDescriptor.Elements.Select(x => x.TemplateCode);
            var newVersion = new Object
                {
                    Id = id,
                    TemplateId = objectDescriptor.TemplateId,
                    TemplateVersionId = objectDescriptor.TemplateVersionId,
                    VersionIndex = prevVersionIndex + 1 ?? 0,
                    Data = JsonConvert.SerializeObject(objectPersistenceDescriptor, SerializerSettings.Default),
                    Author = authorInfo.Author,
                    AuthorLogin = authorInfo.AuthorLogin,
                    AuthorName = authorInfo.AuthorName,
                    ModifiedElements = modifiedElements.ToArray(),
                    LastModified = DateTime.UtcNow
                };

            using (var scope = await _context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
            {
                await _context.ObjectElements.AddRangeAsync(newElementsVersions);
                await _context.Objects.AddAsync(newVersion);

                IReadOnlyCollection<ObjectElementLink> existedElementVersions = Array.Empty<ObjectElementLink>();
                if (!string.IsNullOrEmpty(oldVersionId))
                {
                    existedElementVersions = await _context.ObjectElementLinks
                                                           .AsNoTracking()
                                                           .Where(x => x.ObjectId == id && x.ObjectVersionId == oldVersionId)
                                                           .ToListAsync();
                }

                var elementVersions = existedElementVersions.ToDictionary(x => x.ElementId, x => x.ElementVersionId);
                foreach (var newElementsVersion in newElementsVersions)
                {
                    elementVersions[newElementsVersion.Id] = newElementsVersion.VersionId;
                }

                if (objectElementIdsToSave != null)
                {
                    var objectElementKeysToSave = new HashSet<long>(objectElementIdsToSave);
                    elementVersions = elementVersions.Where(x => objectElementKeysToSave.Contains(x.Key))
                                                     .ToDictionary(x => x.Key, x => x.Value);
                }

                var newLinks = elementVersions.Select(x => new ObjectElementLink
                    {
                        ObjectId = id,
                        ObjectVersionId = newVersion.VersionId,
                        ElementId = x.Key,
                        ElementVersionId = x.Value
                    });

                await _context.ObjectElementLinks.AddRangeAsync(newLinks);
                await _context.SaveChangesAsync();
                scope.Commit();
            }

            _referencedBinariesMetric.Inc(totalBinariesCount);
            return newVersion.VersionId;
        }

        private static (IObjectElementValue elementPersistenceValue, int binariesCount) ConvertToPersistenceValue(
            IObjectElementValue elementValue,
            IReadOnlyDictionary<string, BinaryMetadata> metadataForBinaries)
        {
            if (!(elementValue is IBinaryElementValue binaryElementValue))
            {
                return (elementValue, 0);
            }

            if (string.IsNullOrEmpty(binaryElementValue.Raw))
            {
                return (BinaryElementPersistenceValue.Empty, 0);
            }

            var metadata = metadataForBinaries[binaryElementValue.Raw];
            switch (binaryElementValue)
            {
                case ICompositeBitmapImageElementValue compositeBitmapImageElementValue:
                    {
                        var sizeSpecificImages =
                            compositeBitmapImageElementValue.SizeSpecificImages
                                                            .Select(image =>
                                                                        {
                                                                            var imageMetadata = metadataForBinaries[image.Raw];
                                                                            return new CompositeBitmapImageElementPersistenceValue.SizeSpecificImage
                                                                                {
                                                                                    Filename = imageMetadata.Filename,
                                                                                    Filesize = imageMetadata.FileSize,
                                                                                    Raw = image.Raw,
                                                                                    Size = image.Size
                                                                                };
                                                                        })
                                                            .ToList();
                        var persistenceValue = new CompositeBitmapImageElementPersistenceValue(
                            compositeBitmapImageElementValue.Raw,
                            metadata.Filename,
                            metadata.FileSize,
                            compositeBitmapImageElementValue.CropArea,
                            sizeSpecificImages);
                        return (persistenceValue, sizeSpecificImages.Count + 1);
                    }

                case IScalableBitmapImageElementValue scalableBitmapImageElementValue:
                    {
                        var persistenceValue = new ScalableBitmapImageElementPersistenceValue(
                            scalableBitmapImageElementValue.Raw,
                            metadata.Filename,
                            metadata.FileSize,
                            scalableBitmapImageElementValue.Anchor);
                        return (persistenceValue, 1);
                    }

                default:
                    return (new BinaryElementPersistenceValue(binaryElementValue.Raw, metadata.Filename, metadata.FileSize), 1);
            }
        }

        private void PreprocessObjectElements(IEnumerable<IObjectElementDescriptor> elementDescriptors)
        {
            foreach (var descriptor in elementDescriptors)
            {
                switch (descriptor.Type)
                {
                    case ElementDescriptorType.PlainText:
                        ((TextElementValue)descriptor.Value).Raw =
                            ElementTextHarmonizer.ProcessPlain(((TextElementValue)descriptor.Value).Raw);
                        break;
                    case ElementDescriptorType.FormattedText:
                        ((TextElementValue)descriptor.Value).Raw =
                            ElementTextHarmonizer.ProcessFormatted(((TextElementValue)descriptor.Value).Raw);
                        break;
                    case ElementDescriptorType.FasComment:
                        ((FasElementValue)descriptor.Value).Text =
                            ElementTextHarmonizer.ProcessPlain(((FasElementValue)descriptor.Value).Text);
                        break;
                    case ElementDescriptorType.VideoLink:
                    case ElementDescriptorType.Link:
                        ((TextElementValue)descriptor.Value).Raw =
                            ElementTextHarmonizer.ProcessLink(((TextElementValue)descriptor.Value).Raw);
                        break;
                    case ElementDescriptorType.BitmapImage:
                    case ElementDescriptorType.VectorImage:
                    case ElementDescriptorType.Article:
                    case ElementDescriptorType.Phone:
                    case ElementDescriptorType.Color:
                    case ElementDescriptorType.CompositeBitmapImage:
                    case ElementDescriptorType.ScalableBitmapImage:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(descriptor.Type),
                            descriptor.Type,
                            $"Unsupported element descriptor type for descriptor {descriptor.Id}");
                }
            }
        }

        private async Task<(IReadOnlyDictionary<string, BinaryMetadata> resultMetadata, IReadOnlyDictionary<int, IReadOnlyCollection<ObjectElementValidationError>> errors)>
            RetrieveMetadataForBinaries(
                IEnumerable<IObjectElementDescriptor> existingObjectElements,
                IEnumerable<IObjectElementDescriptor> objectElements)
        {
            var existingFileKeys = new HashSet<string>(existingObjectElements.SelectMany(x => x.Value.ExtractFileKeys()));
            var fileKeysToElementsMap = objectElements.SelectMany(x => x.Value
                                                                        .ExtractFileKeys()
                                                                        .Select(y => (TemplateCode: x.TemplateCode, FileKey: y)))
                                                      .ToLookup(x => x.FileKey, x => x.TemplateCode);

            var tasks = fileKeysToElementsMap.Select(async e =>
                                                         {
                                                             try
                                                             {
                                                                 if (!existingFileKeys.Contains(e.Key))
                                                                 {
                                                                     await _sessionStorageReader.VerifySessionExpirationForBinary(e.Key);
                                                                 }

                                                                 var metadata = await _sessionStorageReader.GetBinaryMetadata(e.Key);
                                                                 return e.Select(templateCode => (TemplateCode: templateCode, FileKey: e.Key, Metadata: metadata,
                                                                                                     Error: (ObjectElementValidationError)null));
                                                             }
                                                             catch (Exception ex) when (ex is ObjectNotFoundException || ex is SessionExpiredException)
                                                             {
                                                                 return e.Select(templateCode => (TemplateCode: templateCode, FileKey: e.Key, Metadata: (BinaryMetadata)null,
                                                                                                     Error: (ObjectElementValidationError)new BinaryNotFoundError(e.Key)));
                                                             }
                                                         })
                                             .ToList();

            var results = (await Task.WhenAll(tasks))
                          .SelectMany(x => x)
                          .ToList();

            var errors = results.Where(x => x.Error != null)
                                .GroupBy(x => x.TemplateCode, x => x.Error)
                                .ToDictionary(x => x.Key, x => (IReadOnlyCollection<ObjectElementValidationError>)x.ToList());

            var resultMetadata = results.Where(x => x.Metadata != null)
                                  .GroupBy(x => x.FileKey)
                                  .ToDictionary(x => x.Key, x => x.First().Metadata);

            return (resultMetadata, errors);
        }
    }
}
