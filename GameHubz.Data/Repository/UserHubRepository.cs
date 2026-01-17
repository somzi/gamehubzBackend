using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class UserHubRepository : BaseRepository<ApplicationContext, UserHubEntity>, IUserHubRepository
    {
        public UserHubRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<Guid>> GetHubIdsByUserId(Guid userId)
        {
            return await this.BaseDbSet()
                .Where(uh => uh.UserId == userId && uh.HubId != null)
                .Select(x => x.HubId!.Value)
                .ToListAsync();
        }
    }
}