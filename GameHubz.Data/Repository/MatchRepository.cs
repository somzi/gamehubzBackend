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

        public Task<List<GroupMatchStatsRow>> GetCompletedSoloMatchStatsForGroup(Guid stageId, Guid? groupId, Guid? excludeMatchId)
        {
            return this.BaseDbSet()
                .AsNoTracking()
                .Where(m =>
                    m.TournamentStageId == stageId &&
                    m.TournamentGroupId == groupId &&
                    m.TeamMatchId == null &&
                    m.Status == MatchStatus.Completed &&
                    // Away may be null only for Swiss byes (home gets the free win);
                    // league/group matches always carry both participants.
                    m.HomeParticipantId.HasValue &&
                    (excludeMatchId == null || m.Id != excludeMatchId))
                .Select(m => new GroupMatchStatsRow
                {
                    HomeParticipantId = m.HomeParticipantId!.Value,
                    AwayParticipantId = m.AwayParticipantId,
                    HomeScore = m.HomeUserScore ?? 0,
                    AwayScore = m.AwayUserScore ?? 0,
                    WinnerParticipantId = m.WinnerParticipantId
                })
                .ToListAsync();
        }

        public Task<List<MatchEntity>> GetByTournamentAndRound(Guid tournamentId, int roundNumber)
        {
            return this.BaseDbSet()
                .Where(m => m.TournamentId == tournamentId && m.RoundNumber == roundNumber)
                .ToListAsync();
        }

        // The set of "active" matches for a user — in-progress tournament, not yet finished,
        // round open, and the user is one of the two sides (solo participant or team sub-match
        // player). Shared by the My-Matches list and the Tournaments-tab badge projection.
        private static System.Linq.Expressions.Expression<Func<MatchEntity, bool>> ActiveForUserPredicate(Guid userId, DateTime now)
            => x =>
                x.Tournament!.Status == TournamentStatus.InProgress &&
                (x.Status == MatchStatus.Pending || x.Status == MatchStatus.Scheduled) &&
                // Round must have started: no RoundOpenAt set is fine, but if set it must not be in the future
                (x.RoundOpenAt == null || x.RoundOpenAt <= now) &&
                (
                    // SOLO matches: HomeUserId/AwayUserId are null, fall back to Participant.UserId
                    (x.TeamMatchId == null && x.HomeParticipantId != null && x.AwayParticipantId != null &&
                        (x.HomeParticipant!.UserId == userId || x.AwayParticipant!.UserId == userId))
                    ||
                    // TEAM sub-matches: use the explicit user columns
                    (x.TeamMatchId != null && (x.HomeUserId == userId || x.AwayUserId == userId))
                );

        public async Task<List<MatchBadgeRow>> GetActiveForUserBadge(Guid userId)
        {
            var now = DateTime.UtcNow;

            return await this.BaseDbSet()
                .AsNoTracking()
                .Where(ActiveForUserPredicate(userId, now))
                .Select(x => new MatchBadgeRow
                {
                    Id = x.Id!.Value,
                    Status = x.Status,
                })
                .ToListAsync();
        }

        public async Task<List<MatchOverviewDto>> GetByUser(Guid userId)
        {
            var now = DateTime.UtcNow;

            var matches = await this.BaseDbSet()
                .AsNoTracking()
                .Where(ActiveForUserPredicate(userId, now))
                .Include(x => x.Tournament).ThenInclude(t => t!.Hub)
                .Include(x => x.HomeParticipant).ThenInclude(p => p!.User)
                .Include(x => x.AwayParticipant).ThenInclude(p => p!.User)
                .Include(x => x.HomeUser)
                .Include(x => x.AwayUser)
                .OrderByDescending(x => x.ModifiedOn)
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
                    ((m.TeamMatchId == null && m.HomeParticipantId != null && m.AwayParticipantId != null && (m.HomeParticipant!.UserId == userId || m.AwayParticipant!.UserId == userId))
                    || (m.TeamMatchId != null && m.HomeUserId != null && m.AwayUserId != null && (m.HomeUserId == userId || m.AwayUserId == userId)))
                    && m.Status == MatchStatus.Completed)
                .OrderBy(m => m.ScheduledStartTime == null)
                .ThenByDescending(m => m.ScheduledStartTime)
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
                    ((m.TeamMatchId == null && m.HomeParticipantId != null && m.AwayParticipantId != null && (m.HomeParticipant!.UserId == userId || m.AwayParticipant!.UserId == userId))
                    || (m.TeamMatchId != null && m.HomeUserId != null && m.AwayUserId != null && (m.HomeUserId == userId || m.AwayUserId == userId)))
                    && m.Status == MatchStatus.Completed)
                .OrderByDescending(m => m.ModifiedOn)
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

        public async Task<List<PerformanceV2Dto>> GetPerformanceByUserIdV2(Guid userId)
        {
            // Take the 10 most recent in SQL, then reverse in memory so the caller gets
            // oldest → latest — the order the UI labels expect.
            var recent = await this.BaseDbSet()
                .Where(m =>
                    ((m.TeamMatchId == null && m.HomeParticipantId != null && m.AwayParticipantId != null && (m.HomeParticipant!.UserId == userId || m.AwayParticipant!.UserId == userId))
                    || (m.TeamMatchId != null && m.HomeUserId != null && m.AwayUserId != null && (m.HomeUserId == userId || m.AwayUserId == userId)))
                    && m.Status == MatchStatus.Completed)
                .OrderByDescending(m => m.ScheduledStartTime ?? m.ModifiedOn)
                .Take(10)
                .Select(m => new PerformanceV2Dto
                {
                    Outcome = m.WinnerParticipantId == null
                        ? "D"
                        : ((m.HomeUserId == userId || (m.TeamMatchId == null && m.HomeParticipant!.UserId == userId))
                            ? m.HomeParticipantId
                            : m.AwayParticipantId) == m.WinnerParticipantId
                            ? "W"
                            : "L",
                })
                .ToListAsync();

            return recent;
        }

        public async Task<PlayerStatsDto> GetStatsByUserId(Guid userId)
        {
            var stats = await this.BaseDbSet()
            .Where(m =>
                ((m.TeamMatchId == null && m.HomeParticipantId != null && m.AwayParticipantId != null && (m.HomeParticipant!.UserId == userId || m.AwayParticipant!.UserId == userId))
                || (m.TeamMatchId != null && m.HomeUserId != null && m.AwayUserId != null && (m.HomeUserId == userId || m.AwayUserId == userId)))
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
                    AwayUserId = x.AwayParticipant!.UserId,
                    HomeUserId = x.HomeParticipant!.UserId,
                    AwayUserScore = x.AwayUserScore ?? 0,
                    HomeUserScore = x.HomeUserScore ?? 0,
                    Evidences = x.MatchEvidences.Select(e => e.Url!).ToList(),
                    ScheduledTime = x.ScheduledStartTime,
                    HomeUserAvatarUrl = x.HomeParticipant.User.AvatarUrl,
                    AwayUserAvatarUrl = x.AwayParticipant.User.AvatarUrl,
                    RequireResultApproval = x.Tournament!.RequireResultApproval,
                    ProposedHomeScore = x.ProposedHomeScore,
                    ProposedAwayScore = x.ProposedAwayScore,
                    ProposedByUserId = x.ProposedByUserId,
                    HubOwnerUserId = x.Tournament!.Hub!.UserId,
                    AdminHelpRequested = x.AdminHelpRequested,
                    AdminHelpRequestedByUserId = x.AdminHelpRequestedByUserId,
                })
                .FirstAsync();
        }

        public async Task<List<MatchAdminHelpItemDto>> GetAdminHelpRequests(Guid tournamentId)
        {
            // Direct HomeUserId/AwayUserId cover team sub-matches; solo matches resolve through participants.
            return await this.BaseDbSet()
                .Where(x => x.TournamentId == tournamentId && x.AdminHelpRequested)
                .OrderBy(x => x.AdminHelpRequestedOn)
                .Select(x => new MatchAdminHelpItemDto
                {
                    MatchId = x.Id!.Value,
                    RoundNumber = x.RoundNumber,
                    Status = x.Status,
                    ScheduledStartTime = x.ScheduledStartTime,
                    RequestedByUserId = x.AdminHelpRequestedByUserId,
                    RequestedOn = x.AdminHelpRequestedOn,
                    RequestedByUsername =
                        x.AdminHelpRequestedByUserId == null ? null
                        : x.HomeUser != null && x.HomeUserId == x.AdminHelpRequestedByUserId ? x.HomeUser.Username
                        : x.AwayUser != null && x.AwayUserId == x.AdminHelpRequestedByUserId ? x.AwayUser.Username
                        : x.HomeParticipant != null && x.HomeParticipant.UserId == x.AdminHelpRequestedByUserId ? x.HomeParticipant.User!.Username
                        : x.AwayParticipant != null && x.AwayParticipant.UserId == x.AdminHelpRequestedByUserId ? x.AwayParticipant.User!.Username
                        : null,
                    HomeUserId = x.HomeUserId ?? (x.HomeParticipant != null ? x.HomeParticipant.UserId : null),
                    HomeUsername = x.HomeUser != null ? x.HomeUser.Username
                        : x.HomeParticipant != null && x.HomeParticipant.User != null ? x.HomeParticipant.User.Username : null,
                    HomeAvatarUrl = x.HomeUser != null ? x.HomeUser.AvatarUrl
                        : x.HomeParticipant != null && x.HomeParticipant.User != null ? x.HomeParticipant.User.AvatarUrl : null,
                    AwayUserId = x.AwayUserId ?? (x.AwayParticipant != null ? x.AwayParticipant.UserId : null),
                    AwayUsername = x.AwayUser != null ? x.AwayUser.Username
                        : x.AwayParticipant != null && x.AwayParticipant.User != null ? x.AwayParticipant.User.Username : null,
                    AwayAvatarUrl = x.AwayUser != null ? x.AwayUser.AvatarUrl
                        : x.AwayParticipant != null && x.AwayParticipant.User != null ? x.AwayParticipant.User.AvatarUrl : null,
                })
                .ToListAsync();
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