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
                .Take(count)
                .Select(x => new DashboardActivityDto
                {
                    HubName = x.Hub!.Name,
                    HubAvatarUrl = x.Hub!.AvatarUrl,
                    TournamentName = x.Tournament!.Name,
                    Type = x.Type,
                    CreatedOn = x.CreatedOn!.Value
                })
                .ToListAsync();
        }

        public async Task<EntityListDto<DashboardActivityDto>> GetRecentActivityPaged(List<Guid> hubIds, int pageNumber, int pageSize)
        {
            var query = this.BaseDbSet()
                .Where(x => hubIds.Contains(x.HubId!.Value));

            var count = await query.CountAsync();

            var items = await query
                .OrderByDescending(x => x.CreatedOn)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new DashboardActivityDto
                {
                    HubName = x.Hub!.Name,
                    HubAvatarUrl = x.Hub!.AvatarUrl,
                    TournamentName = x.Tournament!.Name,
                    Type = x.Type,
                    CreatedOn = x.CreatedOn!.Value
                })
                .ToListAsync();

            return new EntityListDto<DashboardActivityDto>(items, count);
        }

        public async Task<IEnumerable<HubActivityEntity>> GetByHubId(Guid entityId)
        {
            return await this.BaseDbSet()
                .Where(x => x.HubId == entityId)
                .ToListAsync();
        }
    }
}