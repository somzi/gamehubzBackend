using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class MatchStreamRepository : BaseRepository<ApplicationContext, MatchStreamEntity>, IMatchStreamRepository
    {
        public MatchStreamRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<MatchStreamEntity?> GetLatestByMatchId(Guid matchId)
        {
            return await this.BaseDbSet()
                .Where(x => x.MatchId == matchId)
                .OrderByDescending(x => x.CreatedOn)
                .FirstOrDefaultAsync();
        }

        public async Task<List<MatchStreamEntity>> GetByMatchId(Guid matchId)
        {
            return await this.BaseDbSet()
                .Where(x => x.MatchId == matchId)
                .OrderByDescending(x => x.CreatedOn)
                .ToListAsync();
        }

        public async Task<MatchStreamEntity?> GetLatestByMatchAndStreamer(Guid matchId, Guid streamerUserId)
        {
            return await this.BaseDbSet()
                .Where(x => x.MatchId == matchId && x.StreamerUserId == streamerUserId)
                .OrderByDescending(x => x.CreatedOn)
                .FirstOrDefaultAsync();
        }
    }
}
