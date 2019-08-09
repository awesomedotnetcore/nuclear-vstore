using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using NuClear.VStore.Http.Core.Controllers;
using NuClear.VStore.ImageRendering;
using NuClear.VStore.Objects;
using NuClear.VStore.S3;

namespace NuClear.VStore.Renderer.Controllers
{
    [ApiVersion("1.0")]
    [Route("raws")]
    public sealed class RawsController : VStoreController
    {
        private readonly RawFileStorageInfoProvider _rawFileStorageInfoProvider;
        private readonly IObjectsStorageReader _objectsStorageReader;

        public RawsController(RawFileStorageInfoProvider rawFileStorageInfoProvider, IObjectsStorageReader objectsStorageReader)
        {
            _rawFileStorageInfoProvider = rawFileStorageInfoProvider;
            _objectsStorageReader = objectsStorageReader;
        }

        /// <summary>
        /// Redirect to raw file by session identifier and file key.
        /// </summary>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="fileKey">File key.</param>
        /// <returns>HTTP code.</returns>
        [HttpGet("{sessionId:guid}/{fileKey}")]
        [ProducesResponseType(302)]
        [ProducesResponseType(404)]
        public IActionResult RedirectToRaw(Guid sessionId, string fileKey) => Redirect(_rawFileStorageInfoProvider.GetRawFileUrl(sessionId, fileKey));

        /// <summary>
        /// Redirect to raw file by object identifier and version.
        /// </summary>
        /// <param name="id">Object identifier.</param>
        /// <param name="versionId">Object version.</param>
        /// <param name="templateCode">Template code of object's element.</param>
        /// <returns>HTTP code.</returns>
        [HttpGet("{id:long}/{versionId}/{templateCode:int}")]
        [ProducesResponseType(302)]
        [ProducesResponseType(typeof(string), 400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RedirectToRaw(long id, string versionId, int templateCode)
        {
            try
            {
                var imageElementValue = await _objectsStorageReader.GetImageElementValue(id, versionId, templateCode);
                return Redirect(_rawFileStorageInfoProvider.GetRawFileUrl(imageElementValue.Raw));
            }
            catch (ObjectNotFoundException)
            {
                return NotFound();
            }
            catch (ObjectElementInvalidTypeException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
