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
                .Include(x => x.HomeUser)
                .Include(x => x.AwayUser)
                .Where(m => (m.HomeUserId == userId || m.AwayUserId == userId)
                            && m.Status == MatchStatus.Finished)
                .OrderByDescending(m => m.ScheduledStartTime)
                .Take(5)
                .Select(m => new MatchListItemDto
                {
                    TournamentName = m.Tournament!.Name,
                    ScheduledTime = m.ScheduledStartTime,
                    OpponentName = m.HomeUserId == userId ? m.AwayUser!.Username : m.HomeUser!.Username,
                    OpponentScore = m.HomeUserId == userId ? m.AwayUserScore : m.HomeUserScore,
                    UserScore = m.HomeUserId == userId ? m.HomeUserScore : m.AwayUserScore,
                    IsWin = m.WinnerUserId == userId,
                })
                .ToListAsync();
        }

        public async Task<PlayerStatsDto> GetStatsByUserId(Guid userId)
        {
            var stats = await this.BaseDbSet()
            .Where(m => (m.HomeUserId == userId || m.AwayUserId == userId) && m.Status == MatchStatus.Finished)
            .GroupBy(_ => 1)
            .Select(g => new PlayerStatsDto
            {
                TotalMatches = g.Count(),
                Wins = g.Count(m => m.WinnerUserId == userId),
                Losses = g.Count(m => m.WinnerUserId != null && m.WinnerUserId != userId)
            })
             .FirstOrDefaultAsync();

            return stats ?? new PlayerStatsDto();
        }
    }
}