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

        public async Task<bool> AreAllMatchesFinishedInTournament(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .AnyAsync(m => m.TournamentId == tournamentId && m.Status != MatchStatus.Completed);
        }

        public async Task<MatchAvailabilityDto> GetAvailability(Guid id, Guid userId)
        {
            return await this.BaseDbSet()
                .Where(x => x.Id == id)
                .Select(x => new MatchAvailabilityDto
                {
                    MatchId = x.Id!.Value,
                    MySlotsJson = x.HomeParticipant!.UserId == userId
                         ? x.HomeSlotsJson
                         : x.AwaySlotsJson,
                    OpponentSlotsJson = x.HomeParticipant!.UserId == userId
                         ? x.AwaySlotsJson
                         : x.HomeSlotsJson
                })
                .FirstAsync();
        }

        public Task<List<MatchEntity>> GetByStageId(Guid groupStageId)
        {
            return this.BaseDbSet()
                .Where(m => m.TournamentStageId == groupStageId)
                .ToListAsync();
        }

        public async Task<List<MatchOverviewDto>> GetByUser(Guid userId)
        {
            return await this.BaseDbSet()
                .Where(x =>
                    (x.HomeParticipant!.UserId == userId ||
                     x.AwayParticipant!.UserId == userId) &&
                    (x.Status == MatchStatus.Pending ||
                     (x.Status == MatchStatus.Scheduled && x.ScheduledStartTime != null)))
                .Select(x => new MatchOverviewDto
                {
                    HubName = x.Tournament!.Hub!.Name,
                    OpponentName =
                        x.HomeParticipant!.UserId == userId
                            ? x.AwayParticipant!.User!.Username
                            : x.HomeParticipant!.User!.Username,
                    ScheduledTime = x.ScheduledStartTime,
                    TournamentName = x.Tournament!.Name,
                    Status = x.Status,
                    Id = x.Id!.Value,
                    AwayParticipantId = x.AwayParticipantId,
                    HomeParticipantId = x.HomeParticipantId,
                    TournamentId = x.TournamentId
                })
                .ToListAsync();
        }

        public Task<MatchUploadDto> GetForMatchEvidence(Guid matchId)
        {
            return this.BaseDbSet()
                                .Where(x => x.Id == matchId)
                .Select(x => new MatchUploadDto
                {
                    Id = x.Id!.Value,
                    HubName = x.Tournament!.Hub!.Name,
                    TournamentName = x.Tournament!.Name
                })
                .FirstAsync();
        }

        public async Task<List<MatchListItemDto>> GetLastMatchesByUserId(Guid userId, int pageSize, int pageNumber)
        {
            return await this.BaseDbSet()
                .Where(m => (m.HomeParticipant!.UserId == userId || m.AwayParticipant!.UserId == userId)
                            && m.Status == MatchStatus.Completed)
                .OrderByDescending(m => m.ScheduledStartTime)
                .Skip(pageNumber * pageSize)
                .Take(pageSize)
                .Select(m => new MatchListItemDto
                {
                    HubName = m.Tournament!.Hub!.Name,
                    TournamentName = m.Tournament!.Name,
                    ScheduledTime = m.ScheduledStartTime,
                    OpponentName = m.HomeParticipant!.UserId == userId
                        ? m.AwayParticipant!.User!.Username
                        : m.HomeParticipant!.User!.Username,
                    OpponentScore = m.HomeParticipant!.UserId == userId
                        ? m.AwayUserScore
                        : m.HomeUserScore,
                    UserScore = m.HomeParticipant!.UserId == userId
                        ? m.HomeUserScore
                        : m.AwayUserScore,
                    IsWin = m.WinnerParticipant!.UserId == userId,
                })
                .ToListAsync();
        }

        public async Task<List<PerformanceDto>> GetPerformanceByUserId(Guid userId)
        {
            return await this.BaseDbSet()
                .Where(m => (m.HomeParticipant!.UserId == userId || m.AwayParticipant!.UserId == userId)
                            && m.Status == MatchStatus.Completed)
                .OrderByDescending(m => m.ScheduledStartTime)
                .Take(10)
                .Select(m => new PerformanceDto
                {
                    IsWin = m.WinnerParticipant!.UserId == userId,
                })
                .ToListAsync();
        }

        public async Task<PlayerStatsDto> GetStatsByUserId(Guid userId)
        {
            var stats = await this.BaseDbSet()
            .Where(m => (m.HomeParticipant!.UserId == userId || m.AwayParticipant!.UserId == userId) && m.Status == MatchStatus.Completed)
            .GroupBy(_ => 1)
            .Select(g => new PlayerStatsDto
            {
                TotalMatches = g.Count(),
                Wins = g.Count(m => m.WinnerParticipantId != null && m.WinnerParticipant!.UserId == userId),
                Losses = g.Count(m => m.WinnerParticipantId != null && m.WinnerParticipant!.UserId != userId)
            })
             .FirstOrDefaultAsync();

            return stats ?? new PlayerStatsDto();
        }

        public async Task<MatchResultDetailDto> GetWithEvidence(Guid id)
        {
            return await this.BaseDbSet()
                .Where(x => x.Id == id)
                .Select(x => new MatchResultDetailDto
                {
                    AwayUser = x.AwayParticipant!.User!.Nickname,
                    HomeUser = x.HomeParticipant!.User!.Nickname,
                    AwayUserScore = x.AwayUserScore ?? 0,
                    HomeUserScore = x.HomeUserScore ?? 0,
                    Evidences = x.MatchEvidences.Select(e => e.Url!).ToList(),
                    ScheduledTime = x.ScheduledStartTime
                })
                .FirstAsync();
        }

        public async Task<MatchEntity?> GetWithParticipants(Guid matchId)
        {
            return await this.BaseDbSet()
                .Include(x => x.HomeParticipant)
                .Include(x => x.AwayParticipant)
                .Where(x => x.Id == matchId)
                .FirstOrDefaultAsync();
        }

        public async Task<MatchEntity?> GetWithStage(Guid id)
        {
            return await this.BaseDbSet()
                .Include(x => x.TournamentStage)
                .Include(x => x.HomeParticipant)
                .Include(x => x.AwayParticipant)
                .Where(x => x.Id == id)
                .FirstOrDefaultAsync();
        }

        public Task<MatchEntity?> GetWithTournamentStage(Guid id)
        {
            return this.BaseDbSet().Include(x => x.TournamentStage)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<bool> HasMatchesForStage(Guid stageId)
        {
            return await this.BaseDbSet()
                .AnyAsync(m => m.TournamentStageId == stageId);
        }

        public async Task<bool> IsExistingByStageId(Guid? bracketStageId)
        {
            return await this.BaseDbSet()
                .AnyAsync(m => m.TournamentStageId == bracketStageId);
        }
    }
}