using Template.Data.Base;
using Template.Data.Context;
using Template.DataModels.Domain;
using Template.Logic.Interfaces;
using Template.Logic.Utility;

namespace Template.Data.Repository
{
    public class AssetRepository : BaseRepository<ApplicationContext, AssetEntity>, IAssetRepository
    {
        public AssetRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }
    }
}