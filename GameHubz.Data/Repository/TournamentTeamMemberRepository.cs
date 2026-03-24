using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class TournamentTeamMemberRepository : BaseRepository<ApplicationContext, TournamentTeamMemberEntity>, ITournamentTeamMemberRepository
    {
        public TournamentTeamMemberRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<TournamentTeamMemberEntity>> GetByTeamId(Guid teamId)
        {
            return await this.BaseDbSet()
                .Where(m => m.TeamId == teamId)
                .Include(m => m.User)
                .ToListAsync();
        }

        public async Task<List<TournamentTeamMemberEntity>> GetByUserId(Guid userId)
        {
            return await this.BaseDbSet()
                .Where(m => m.UserId == userId)
                .Include(m => m.Team)
                .ToListAsync();
        }

        public async Task<bool> ExistsInTournament(Guid userId, Guid tournamentId)
        {
            return await this.BaseDbSet()
                .AnyAsync(m => m.UserId == userId && m.Team!.TournamentId == tournamentId);
        }
    }
}
