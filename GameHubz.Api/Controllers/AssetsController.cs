using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using GameHubz.Common.Models;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AssetsController : ControllerBase
    {
        private readonly AssetService assetService;
        private readonly ILocalizationService localizationService;

        public AssetsController(
            AssetService assetService,
            ILocalizationService localizationService)
        {
            this.assetService = assetService;
            this.localizationService = localizationService;
        }

        [HttpGet("{id}")]
        public virtual async Task<Asset> GetById(
            [FromRoute, Required] Guid id)
        {
            var model = await this.assetService.GetEntityById(id);

            return model;
        }

        [HttpGet("")]
        public virtual async Task<EntityListDto<Asset>> GetList(
            [FromQuery] int? pageIndex,
            [FromQuery] int? pageSize,
            [FromQuery] List<SortItem> sortItems,
            [FromQuery] List<FilterItem> filterItems)
        {
            EntityListDto<Asset> list = await this.assetService.GetEntities(
                filterItems,
                sortItems,
                pageIndex,
                pageSize);

            return list;
        }

        [HttpPost("")]
        public async Task<Asset> UploadAsset([FromForm] AssetUpload assetUpload)
        {
            if (assetUpload is null || assetUpload.File is null)
            {
                throw new ArgumentNullException(nameof(assetUpload));
            }

            if (assetUpload.File.Length <= 0)
            {
                throw new Exception(this.localizationService["Exception.FileSizeZeroBytesException"]);
            }

            Asset asset = await this.assetService.UploadAsset(assetUpload);

            return asset;
        }

        [HttpPost("{assetId}")]
        public async Task<IActionResult> UpdateAsset([FromRoute, Required] Guid assetId, [FromBody] AssetUpdate assetUpdate)
        {
            await this.assetService.UpdateAsset(assetUpdate, assetId);
            return this.Ok();
        }

        [HttpDelete("{assetId}")]
        public async Task<IActionResult> DeleteAssetById([FromRoute, Required] Guid assetId)
        {
            await this.assetService.DeleteAsset(assetId);
            return this.Ok();
        }
    }
}
