using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class MatchRepository : BaseRepository<ApplicationContext, MatchEntity>, IMatchRepository
    {
        public MatchRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<List<MatchListItemDto>> GetLastMatchesByUserId(Guid userId)
        {
            return await this.BaseDbSet()
                .Include(x => x.Tournament)
                .Include(x => x.HomeParticipant)
                    .ThenInclude(x => x!.User)
                .Include(x => x.AwayParticipant)
                    .ThenInclude(x => x!.User)
                .Where(m => (m.HomeParticipantId == userId || m.AwayParticipantId == userId)
                            && m.Status == MatchStatus.Completed)
                .OrderByDescending(m => m.ScheduledStartTime)
                .Take(5)
                .Select(m => new MatchListItemDto
                {
                    TournamentName = m.Tournament!.Name,
                    ScheduledTime = m.ScheduledStartTime,
                    OpponentName = m.HomeParticipantId == userId ? m.AwayParticipant!.User!.Username : m.HomeParticipant!.User!.Username,
                    OpponentScore = m.HomeParticipantId == userId ? m.AwayUserScore : m.HomeUserScore,
                    UserScore = m.HomeParticipantId == userId ? m.HomeUserScore : m.AwayUserScore,
                    IsWin = m.WinnerParticipantId == userId,
                })
                .ToListAsync();
        }

        public async Task<PlayerStatsDto> GetStatsByUserId(Guid userId)
        {
            var stats = await this.BaseDbSet()
            .Where(m => (m.HomeParticipantId == userId || m.AwayParticipantId == userId) && m.Status == MatchStatus.Completed)
            .GroupBy(_ => 1)
            .Select(g => new PlayerStatsDto
            {
                TotalMatches = g.Count(),
                Wins = g.Count(m => m.WinnerParticipantId == userId),
                Losses = g.Count(m => m.WinnerParticipantId != null && m.WinnerParticipantId != userId)
            })
             .FirstOrDefaultAsync();

            return stats ?? new PlayerStatsDto();
        }

        public async Task<MatchEntity?> GetWithStage(Guid id)
        {
            return await this.BaseDbSet()
                .Include(x => x.TournamentStage)
                .Include(x => x.Id == id)
                .FirstOrDefaultAsync();
        }
    }
}