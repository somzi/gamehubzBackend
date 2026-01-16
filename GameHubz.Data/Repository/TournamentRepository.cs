using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class TournamentRepository : BaseRepository<ApplicationContext, TournamentEntity>, ITournamentRepository
    {
        public TournamentRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<TournamentEntity?> GetWithParticipents(Guid id)
        {
            return await this.BaseDbSet()
                .Include(x => x.TournamentParticipants)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<List<TournamentEntity>> GetByHubPaged(Guid hubId, TournamentStatus status, int page, int pageSize)
        {
            var query = this.BaseDbSet()
                .Where(x => x.HubId == hubId && x.Status == status);

            var items = await query
                .OrderByDescending(x => x.StartDate)
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return items;
        }

        public async Task<int> GetByHubCount(Guid hubId, TournamentStatus status)
        {
            var query = this.BaseDbSet()
                .Where(x => x.HubId == hubId && x.Status == status);

            return await query.CountAsync();
        }
    }
}