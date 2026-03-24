using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class TeamMatchRepository : BaseRepository<ApplicationContext, TeamMatchEntity>, ITeamMatchRepository
    {
        public TeamMatchRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<TeamMatchEntity?> GetByIdWithSubMatches(Guid teamMatchId)
        {
            return await this.BaseDbSet()
                .Where(tm => tm.Id == teamMatchId)
                .Include(tm => tm.SubMatches)
                    .ThenInclude(m => m.HomeParticipant)
                        .ThenInclude(p => p!.User)
                .Include(tm => tm.SubMatches)
                    .ThenInclude(m => m.AwayParticipant)
                        .ThenInclude(p => p!.User)
                .Include(tm => tm.SubMatches)
                    .ThenInclude(m => m.HomeUser)
                .Include(tm => tm.SubMatches)
                    .ThenInclude(m => m.AwayUser)
                .Include(tm => tm.HomeTeamParticipant)
                    .ThenInclude(p => p!.Team)
                        .ThenInclude(t => t!.Members)
                            .ThenInclude(m => m.User)
                .Include(tm => tm.AwayTeamParticipant)
                    .ThenInclude(p => p!.Team)
                        .ThenInclude(t => t!.Members)
                            .ThenInclude(m => m.User)
                .FirstOrDefaultAsync();
        }

        public async Task<List<TeamMatchEntity>> GetByStageId(Guid stageId)
        {
            return await this.BaseDbSet()
                .Where(tm => tm.TournamentStageId == stageId)
                .Include(tm => tm.SubMatches)
                .ToListAsync();
        }
    }
}
