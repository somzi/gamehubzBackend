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
            var hasUnfinished = await this.BaseDbSet()
                .AnyAsync(m => m.TournamentId == tournamentId && m.Status != MatchStatus.Completed);

            return !hasUnfinished;
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
                         : x.HomeSlotsJson,
                    MatchDeadline = x.RoundDeadline
                })
                .FirstAsync();
        }

        public Task<List<MatchEntity>> GetByStageId(Guid groupStageId)
        {
            return this.BaseDbSet()
                .Where(m => m.TournamentStageId == groupStageId)
                .ToListAsync();
        }

        public Task<List<MatchEntity>> GetByTournamentAndRound(Guid tournamentId, int roundNumber)
        {
            return this.BaseDbSet()
                .Where(m => m.TournamentId == tournamentId && m.RoundNumber == roundNumber)
                .ToListAsync();
        }

        public async Task<List<MatchOverviewDto>> GetByUser(Guid userId)
        {
            var now = DateTime.UtcNow;

            var matches = await this.BaseDbSet()
                .AsNoTracking()
                .Where(x =>
                    x.Tournament!.Status == TournamentStatus.InProgress &&
                    (x.Status == MatchStatus.Pending || x.Status == MatchStatus.Scheduled) &&
                    (
                        // SOLO matches: HomeUserId/AwayUserId are null, fall back to Participant.UserId
                        (x.TeamMatchId == null && x.HomeParticipantId != null && x.AwayParticipantId != null &&
                            (x.HomeParticipant!.UserId == userId || x.AwayParticipant!.UserId == userId))
                        ||
                        // TEAM sub-matches: use the explicit user columns
                        (x.TeamMatchId != null && (x.HomeUserId == userId || x.AwayUserId == userId))
                    )
                )
                .Include(x => x.Tournament).ThenInclude(t => t!.Hub)
                .Include(x => x.HomeParticipant).ThenInclude(p => p!.User)
                .Include(x => x.AwayParticipant).ThenInclude(p => p!.User)
                .Include(x => x.HomeUser)
                .Include(x => x.AwayUser)
                .OrderByDescending(x => x.ScheduledStartTime)
                .ToListAsync();

            var result = new List<MatchOverviewDto>();

            foreach (var match in matches)
            {
                // Resolve actual user IDs: prefer explicit columns, fall back to participant
                Guid? homeUserId = match.HomeUserId ?? match.HomeParticipant?.UserId;
                Guid? awayUserId = match.AwayUserId ?? match.AwayParticipant?.UserId;

                if (homeUserId != userId && awayUserId != userId)
                    continue;

                bool iAmHome = homeUserId == userId;

                // Resolve user info: prefer HomeUser/AwayUser, fall back to Participant.User
                var homeUser = match.HomeUser ?? match.HomeParticipant?.User;
                var awayUser = match.AwayUser ?? match.AwayParticipant?.User;

                var me = iAmHome ? homeUser : awayUser;
                var opponent = iAmHome ? awayUser : homeUser;

                result.Add(new MatchOverviewDto
                {
                    Id = match.Id!.Value,
                    HubName = match.Tournament!.Hub!.Name,
                    TournamentName = match.Tournament.Name,
                    TournamentId = match.TournamentId,
                    Status = match.Status,
                    ScheduledTime = match.ScheduledStartTime,
                    UserNickname = me?.Nickname ?? me?.Username ?? "Unknown",
                    OpponentName = opponent?.Username ?? "Unknown",
                    OpponentNickname = opponent?.Nickname ?? opponent?.Username ?? "Unknown",
                    OpponentAvatarUrl = opponent?.AvatarUrl
                });
            }

            return result;
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
                .Where(m =>
                    ((m.TeamMatchId == null && (m.HomeParticipant!.UserId == userId || m.AwayParticipant!.UserId == userId))
                    || (m.TeamMatchId != null && (m.HomeUserId == userId || m.AwayUserId == userId)))
                    && m.Status == MatchStatus.Completed)
                .OrderByDescending(m => m.ScheduledStartTime)
                .Skip(pageNumber * pageSize)
                .Take(pageSize)
                .Select(m => new MatchListItemDto
                {
                    HubName = m.Tournament!.Hub!.Name,
                    TournamentName = m.Tournament!.Name,
                    ScheduledTime = m.ScheduledStartTime,
                    OpponentName = (m.HomeUserId == userId || (m.TeamMatchId == null && m.HomeParticipant!.UserId == userId))
                        ? (m.AwayUser != null ? m.AwayUser.Username
                            : (m.AwayParticipant != null && m.AwayParticipant.User != null ? m.AwayParticipant.User.Username : "Unknown"))
                        : (m.HomeUser != null ? m.HomeUser.Username
                            : (m.HomeParticipant != null && m.HomeParticipant.User != null ? m.HomeParticipant.User.Username : "Unknown")),
                    OpponentAvatarUrl = (m.HomeUserId == userId || (m.TeamMatchId == null && m.HomeParticipant!.UserId == userId))
                        ? (m.AwayUser != null ? m.AwayUser.AvatarUrl
                            : (m.AwayParticipant != null && m.AwayParticipant.User != null ? m.AwayParticipant.User.AvatarUrl : null))
                        : (m.HomeUser != null ? m.HomeUser.AvatarUrl
                            : (m.HomeParticipant != null && m.HomeParticipant.User != null ? m.HomeParticipant.User.AvatarUrl : null)),
                    OpponentScore = (m.HomeUserId == userId || (m.TeamMatchId == null && m.HomeParticipant!.UserId == userId))
                        ? m.AwayUserScore
                        : m.HomeUserScore,
                    UserScore = (m.HomeUserId == userId || (m.TeamMatchId == null && m.HomeParticipant!.UserId == userId))
                        ? m.HomeUserScore
                        : m.AwayUserScore,
                    UserAvatarUrl = (m.HomeUserId == userId || (m.TeamMatchId == null && m.HomeParticipant!.UserId == userId))
                        ? (m.HomeUser != null ? m.HomeUser.AvatarUrl
                            : (m.HomeParticipant != null && m.HomeParticipant.User != null ? m.HomeParticipant.User.AvatarUrl : null))
                        : (m.AwayUser != null ? m.AwayUser.AvatarUrl
                            : (m.AwayParticipant != null && m.AwayParticipant.User != null ? m.AwayParticipant.User.AvatarUrl : null)),
                    Username = (m.HomeUserId == userId || (m.TeamMatchId == null && m.HomeParticipant!.UserId == userId))
                        ? (m.HomeUser != null ? m.HomeUser.Username
                            : (m.HomeParticipant != null && m.HomeParticipant.User != null ? m.HomeParticipant.User.Username : "Unknown"))
                        : (m.AwayUser != null ? m.AwayUser.Username
                            : (m.AwayParticipant != null && m.AwayParticipant.User != null ? m.AwayParticipant.User.Username : "Unknown")),
                    IsWin = m.WinnerParticipantId != null &&
                        ((m.HomeUserId == userId || (m.TeamMatchId == null && m.HomeParticipant!.UserId == userId))
                            ? m.WinnerParticipantId == m.HomeParticipantId
                            : m.WinnerParticipantId == m.AwayParticipantId),
                })
                .ToListAsync();
        }

        public async Task<List<PerformanceDto>> GetPerformanceByUserId(Guid userId)
        {
            return await this.BaseDbSet()
                .Where(m =>
                    ((m.TeamMatchId == null && (m.HomeParticipant!.UserId == userId || m.AwayParticipant!.UserId == userId))
                    || (m.TeamMatchId != null && (m.HomeUserId == userId || m.AwayUserId == userId)))
                    && m.Status == MatchStatus.Completed)
                .OrderByDescending(m => m.ScheduledStartTime)
                .Take(10)
                .Select(m => new PerformanceDto
                {
                    IsWin = m.WinnerParticipantId != null &&
                        ((m.HomeUserId == userId || (m.TeamMatchId == null && m.HomeParticipant!.UserId == userId))
                            ? m.WinnerParticipantId == m.HomeParticipantId
                            : m.WinnerParticipantId == m.AwayParticipantId),
                })
                .ToListAsync();
        }

        public async Task<PlayerStatsDto> GetStatsByUserId(Guid userId)
        {
            var stats = await this.BaseDbSet()
            .Where(m =>
                ((m.TeamMatchId == null && (m.HomeParticipant!.UserId == userId || m.AwayParticipant!.UserId == userId))
                || (m.TeamMatchId != null && (m.HomeUserId == userId || m.AwayUserId == userId)))
                && m.Status == MatchStatus.Completed)
            .GroupBy(_ => 1)
            .Select(g => new PlayerStatsDto
            {
                TotalMatches = g.Count(),
                Wins = g.Count(m => m.WinnerParticipantId != null &&
                    ((m.HomeUserId == userId || (m.TeamMatchId == null && m.HomeParticipant!.UserId == userId))
                        ? m.WinnerParticipantId == m.HomeParticipantId
                        : m.WinnerParticipantId == m.AwayParticipantId)),
                Losses = g.Count(m => m.WinnerParticipantId != null &&
                    ((m.HomeUserId == userId || (m.TeamMatchId == null && m.HomeParticipant!.UserId == userId))
                        ? m.WinnerParticipantId != m.HomeParticipantId
                        : m.WinnerParticipantId != m.AwayParticipantId))
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
                    AwayUser = x.AwayParticipant!.User!.Nickname ?? "unknown",
                    HomeUser = x.HomeParticipant!.User!.Nickname ?? "unknown",
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
                    .ThenInclude(p => p!.Team)
                        .ThenInclude(t => t!.Members)
                .Include(x => x.AwayParticipant)
                    .ThenInclude(p => p!.Team)
                        .ThenInclude(t => t!.Members)
                .Where(x => x.Id == matchId)
                .FirstOrDefaultAsync();
        }

        public async Task<MatchEntity?> GetWithStage(Guid id)
        {
            return await this.BaseDbSet()
                .Include(x => x.TournamentStage)
                .Include(x => x.HomeParticipant)
                    .ThenInclude(p => p!.Team)
                        .ThenInclude(t => t!.Members)
                .Include(x => x.AwayParticipant)
                    .ThenInclude(p => p!.Team)
                        .ThenInclude(t => t!.Members)
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