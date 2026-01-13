using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Template.Logic.Services
{
    public class AssetService : AppBaseService
    {
        private readonly BlobService blobService;
        private readonly ImageService imageService;
        private readonly ILogger<AssetService> logger;
        private readonly IMapper mapper;
        private readonly IValidator<AssetEntity> validator;
        private readonly ServiceFunctions serviceFunctions;

        public AssetService(
            IUnitOfWorkFactory factory,
            BlobService blobService,
            IMapper mapper,
            ILogger<AssetService> logger,
            ILocalizationService localizationService,
            ImageService imageService,
            IUserContextReader userContextReader,
            IValidator<AssetEntity> validator,
            ServiceFunctions serviceFunctions)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.validator = validator;
            this.serviceFunctions = serviceFunctions;
            this.mapper = mapper;
            this.logger = logger;
            this.blobService = blobService;
            this.imageService = imageService;
        }

        public async Task<EntityListDto<Asset>> GetEntities(
            IList<FilterItem>? filterItems,
            IList<SortItem>? sortItems,
            int? pageIndex,
            int? pageSize)
        {
            EntityListDto<Asset> searchData = await this.serviceFunctions.GetEntities<AssetEntity, Asset>(
                this.AppUnitOfWork.AssetRepository, filterItems, sortItems, pageIndex, pageSize);

            return searchData;
        }

        public async Task<Asset> GetEntityById(Guid id)
        {
            Asset model = await this.serviceFunctions.GetEntityById<AssetEntity, Asset>(
                this.AppUnitOfWork.AssetRepository, id);

            return model;
        }

        public async Task<Asset> UploadAsset(AssetUpload assetUpload)
        {
            TokenUserInfo? userInfo = await this.UserContextReader.GetTokenUserInfoFromContext();

            string extension = Path.GetExtension(assetUpload.File?.FileName) ?? "";
            string fileName = Path.GetFileNameWithoutExtension(assetUpload.File?.FileName) ?? "";

            string blobName = string.Concat(Guid.NewGuid().ToString(), extension);
            AssetType assetType = (AssetType)assetUpload.AssetTypeId;

            AssetEntity asset = new()
            {
                FileName = fileName,
                BlobName = blobName,
                Extension = extension,
                AssetType = assetType,
                CreatedBy = userInfo?.UserId,
                ModifiedBy = userInfo?.UserId,
                Size = assetUpload.File!.Length,
                Description = assetUpload.Description
            };

            try
            {
                using Stream fileStream = assetUpload.File.OpenReadStream();

                var blobInfo = await this.blobService.Upload(
                                    fileStream,
                                    containerName: BlobUrlHelper.GetContainerName(assetType),
                                    blobName: blobName);

                string thumbnailUrl = "";

                if (BlobUrlHelper.HasThumbnail(assetType))
                {
                    BlobInfo? thumbBlobInfo = await this.imageService.ResizeAndUploadThumbnail(assetUpload.File, blobName);
                    thumbnailUrl = thumbBlobInfo?.Url ?? "";
                }

                this.validator.ValidateAndThrow(asset);

                await this.AppUnitOfWork.AssetRepository.AddUpdateEntity(asset, this.UserContextReader);
                await this.SaveAsync();

                Asset assetModel = this.mapper.Map<Asset>(asset);

                return assetModel;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error in UploadAsset");

                try
                {
                    await this.blobService.Delete(blobName, BlobUrlHelper.GetContainerName(assetType));
                }
                catch (Exception innerEx)
                {
                    this.logger.LogError(innerEx, "Unable to delete file after exception in UploadAsset");
                }

                throw new AssetUploadFailedException();
            }
        }

        public async Task UpdateAsset(AssetUpdate assetUpdate, Guid assetId)
        {
            if (assetUpdate is null)
            {
                throw new ArgumentNullException(nameof(assetUpdate));
            }

            if (assetId == Guid.Empty)
            {
                throw new ArgumentNullException(nameof(assetId));
            }

            AssetEntity? asset = await this.AppUnitOfWork.AssetRepository.GetById(assetId);

            if (asset == null)
            {
                throw new EntityNotFoundException(assetId, typeof(AssetEntity).ToString(), this.LocalizationService);
            }

            asset = this.mapper.Map(assetUpdate, asset, typeof(AssetUpdate), typeof(AssetEntity)) as AssetEntity;

            await this.AppUnitOfWork.AssetRepository.UpdateEntity(asset!, this.UserContextReader);

            await this.SaveAsync();
        }

        public async Task DeleteAsset(Guid id)
        {
            AssetEntity? asset = await this.AppUnitOfWork.AssetRepository.GetById(id);

            if (asset is null)
            {
                throw new EntityNotFoundException(id, typeof(AssetEntity).ToString(), this.LocalizationService);
            }

            await this.AppUnitOfWork.AssetRepository.SoftDeleteEntity(asset, this.UserContextReader);

            await this.SaveAsync();
        }
    }
}