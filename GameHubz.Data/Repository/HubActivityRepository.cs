using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class HubActivityRepository : BaseRepository<ApplicationContext, HubActivityEntity>, IHubActivityRepository
    {
        public HubActivityRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<DashboardActivityDto>> GetRecentActivity(List<Guid> hubIds, int count)
        {
            return await this.BaseDbSet()
                .Where(x => hubIds.Contains(x.HubId!.Value))
                .OrderByDescending(x => x.CreatedOn)
                .Take(100)
                .Select(x => new DashboardActivityDto
                {
                    HubName = x.Hub!.Name,
                    TournamentName = x.Tournament!.Name,
                    Type = x.Type,
                    CreatedOn = x.CreatedOn!.Value
                })
                .ToListAsync();
        }
    }
}