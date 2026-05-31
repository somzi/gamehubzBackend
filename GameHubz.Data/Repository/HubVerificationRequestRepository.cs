using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class HubVerificationRequestRepository : BaseRepository<ApplicationContext, HubVerificationRequestEntity>, IHubVerificationRequestRepository
    {
        public HubVerificationRequestRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public Task<HubVerificationRequestEntity?> GetLatestForHub(Guid hubId)
        {
            return this.BaseDbSet()
                .Where(r => r.HubId == hubId)
                .OrderByDescending(r => r.CreatedOn)
                .FirstOrDefaultAsync();
        }

        public Task<HubVerificationRequestEntity?> GetPendingForHub(Guid hubId)
        {
            return this.BaseDbSet()
                .FirstOrDefaultAsync(r => r.HubId == hubId && r.Status == HubVerificationStatus.Pending);
        }
    }
}
