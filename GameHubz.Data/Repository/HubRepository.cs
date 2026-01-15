using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class HubRepository : BaseRepository<ApplicationContext, HubEntity>, IHubRepository
    {
        public HubRepository(ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<HubEntity>> GetOverview()
        {
            return await this.BaseDbSet()
                .Include(x => x.UserHubs)
                .Include(x => x.Tournaments)
                .ToListAsync();
        }

        public Task<List<HubEntity>> GetWithDetailsById(Guid id)
        {
            throw new NotImplementedException();
        }
    }
}