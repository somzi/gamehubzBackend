using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class TournamentTeamRepository : BaseRepository<ApplicationContext, TournamentTeamEntity>, ITournamentTeamRepository
    {
        public TournamentTeamRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<TournamentTeamEntity>> GetByTournamentId(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(t => t.TournamentId == tournamentId)
                .Include(t => t.Members)
                    .ThenInclude(m => m.User)
                .Include(t => t.CaptainUser)
                .ToListAsync();
        }

        public async Task<List<TournamentTeamEntity>> GetFinalByTournamentId(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(t => t.TournamentId == tournamentId && t.TournamentParticipantId != null)
                .Include(t => t.Members)
                    .ThenInclude(m => m.User)
                .Include(t => t.CaptainUser)
                .ToListAsync();
        }

        public async Task<TournamentTeamEntity?> GetByIdWithMembers(Guid teamId)
        {
            return await this.BaseDbSet()
                .Where(t => t.Id == teamId)
                .Include(t => t.Members)
                    .ThenInclude(m => m.User)
                .Include(t => t.CaptainUser)
                .Include(t => t.Tournament)
                .FirstOrDefaultAsync();
        }

        public async Task<TournamentTeamEntity> GetSingleByTournamentId(Guid tournamentId, Guid userId)
        {
            return await this.BaseDbSet()
                .Where(t => t.TournamentId == tournamentId
                    && t.Members.Any(m => m.UserId == userId))
                .Include(t => t.Members)
                    .ThenInclude(m => m.User)
                .Include(t => t.CaptainUser)
                .Include(t => t.Tournament)
                    .ThenInclude(t => t!.TournamentRegistrations)
                .FirstAsync();
        }
    }
}