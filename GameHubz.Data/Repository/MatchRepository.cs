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

            // Step 1: Find all teams the user belongs to.
            var userTeamIds = await this.ContextBase
                .Set<TournamentTeamMemberEntity>()
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.TeamId.HasValue)
                .Select(m => m.TeamId!.Value)
                .Distinct()
                .ToListAsync();

            // Step 2: Resolve participant IDs for those teams + build participantId→teamId map.
            var userTeamParticipantIds = new HashSet<Guid>();
            var participantToTeamMap = new Dictionary<Guid, Guid>();

            if (userTeamIds.Count > 0)
            {
                var participantData = await this.ContextBase.Set<TournamentParticipantEntity>()
                    .AsNoTracking()
                    .Where(p => p.TeamId.HasValue && userTeamIds.Contains(p.TeamId!.Value))
                    .Select(p => new { ParticipantId = p.Id!.Value, TeamId = p.TeamId!.Value })
                    .ToListAsync();

                foreach (var pd in participantData)
                {
                    userTeamParticipantIds.Add(pd.ParticipantId);
                    participantToTeamMap[pd.ParticipantId] = pd.TeamId;
                }
            }

            var teamParticipantIds = userTeamParticipantIds.ToList();

            // Step 3: Pre-fetch sorted team members for the user's teams.
            var teamMembersMap = new Dictionary<Guid, List<TeamMemberInfo>>();

            if (userTeamIds.Count > 0)
            {
                var allMembers = await this.ContextBase
                    .Set<TournamentTeamMemberEntity>()
                    .AsNoTracking()
                    .Where(m => m.TeamId.HasValue && userTeamIds.Contains(m.TeamId!.Value) && m.UserId.HasValue)
                    .OrderBy(m => m.JoinedAt)
                    .ThenBy(m => m.Id)
                    .Select(m => new
                    {
                        TeamId = m.TeamId!.Value,
                        UserId = m.UserId!.Value,
                        m.User!.Username,
                        m.User.Nickname,
                        m.User.AvatarUrl
                    })
                    .ToListAsync();

                foreach (var m in allMembers)
                {
                    if (!teamMembersMap.TryGetValue(m.TeamId, out var list))
                    {
                        list = [];
                        teamMembersMap[m.TeamId] = list;
                    }
                    list.Add(new TeamMemberInfo(m.UserId, m.Username, m.Nickname, m.AvatarUrl));
                }
            }

            // Step 4: Main query — flat SELECT, no deep Team.Members navigation.
            var matchData = await this.BaseDbSet()
                .AsNoTracking()
                .Where(x =>
                    x.Tournament!.Status == TournamentStatus.InProgress &&
                    x.HomeParticipantId != null &&
                    x.AwayParticipantId != null &&
                    (x.Status == MatchStatus.Pending ||
                     (x.Status == MatchStatus.Scheduled && x.ScheduledStartTime != null)) &&
                    (x.RoundOpenAt == null || x.RoundOpenAt <= now) &&
                    (x.RoundDeadline == null || x.RoundDeadline >= now) &&
                    (
                        x.HomeParticipant!.UserId == userId ||
                        x.AwayParticipant!.UserId == userId ||
                        (teamParticipantIds.Count > 0 && (
                            (x.HomeParticipantId.HasValue && teamParticipantIds.Contains(x.HomeParticipantId.Value)) ||
                            (x.AwayParticipantId.HasValue && teamParticipantIds.Contains(x.AwayParticipantId.Value))
                        ))
                    )
                )
                .Select(x => new
                {
                    x.Id,
                    x.TournamentId,
                    x.HomeParticipantId,
                    x.AwayParticipantId,
                    x.Status,
                    x.ScheduledStartTime,
                    x.MatchOrder,
                    x.TeamMatchId,
                    HubName = x.Tournament!.Hub!.Name,
                    TournamentName = x.Tournament.Name,
                    HomeUserId = x.HomeParticipant!.UserId,
                    HomeUsername = x.HomeParticipant.User!.Username,
                    HomeNickname = x.HomeParticipant.User.Nickname,
                    HomeAvatarUrl = x.HomeParticipant.User.AvatarUrl,
                    AwayUserId = x.AwayParticipant!.UserId,
                    AwayUsername = x.AwayParticipant.User!.Username,
                    AwayNickname = x.AwayParticipant.User.Nickname,
                    AwayAvatarUrl = x.AwayParticipant.User.AvatarUrl,
                })
                .OrderByDescending(x => x.ScheduledStartTime)
                .ToListAsync();

            // Step 5: Pre-fetch opponent team members for matched team sub-matches.
            var opponentParticipantIds = matchData
                .Where(x => x.TeamMatchId.HasValue)
                .SelectMany(x => new[] { x.HomeParticipantId, x.AwayParticipantId })
                .Where(id => id.HasValue && !participantToTeamMap.ContainsKey(id.Value))
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            if (opponentParticipantIds.Count > 0)
            {
                var opponentData = await this.ContextBase.Set<TournamentParticipantEntity>()
                    .AsNoTracking()
                    .Where(p => opponentParticipantIds.Contains(p.Id!.Value) && p.TeamId.HasValue)
                    .Select(p => new { ParticipantId = p.Id!.Value, TeamId = p.TeamId!.Value })
                    .ToListAsync();

                var opponentTeamIds = opponentData
                    .Select(d => d.TeamId)
                    .Where(tid => !teamMembersMap.ContainsKey(tid))
                    .Distinct()
                    .ToList();

                foreach (var d in opponentData)
                    participantToTeamMap.TryAdd(d.ParticipantId, d.TeamId);

                if (opponentTeamIds.Count > 0)
                {
                    var opponentMembers = await this.ContextBase
                        .Set<TournamentTeamMemberEntity>()
                        .AsNoTracking()
                        .Where(m => m.TeamId.HasValue && opponentTeamIds.Contains(m.TeamId!.Value) && m.UserId.HasValue)
                        .OrderBy(m => m.JoinedAt)
                        .ThenBy(m => m.Id)
                        .Select(m => new
                        {
                            TeamId = m.TeamId!.Value,
                            UserId = m.UserId!.Value,
                            m.User!.Username,
                            m.User.Nickname,
                            m.User.AvatarUrl
                        })
                        .ToListAsync();

                    foreach (var m in opponentMembers)
                    {
                        if (!teamMembersMap.TryGetValue(m.TeamId, out var list))
                        {
                            list = [];
                            teamMembersMap[m.TeamId] = list;
                        }
                        list.Add(new TeamMemberInfo(m.UserId, m.Username, m.Nickname, m.AvatarUrl));
                    }
                }
            }

            // Step 6: Build results.
            var result = new List<MatchOverviewDto>(matchData.Count);

            foreach (var item in matchData)
            {
                // --- SOLO or TIE-BREAK (direct participant with UserId) ---
                if (item.HomeUserId == userId || item.AwayUserId == userId)
                {
                    bool isHome = item.HomeUserId == userId;

                    result.Add(new MatchOverviewDto
                    {
                        Id = item.Id!.Value,
                        HubName = item.HubName,
                        TournamentName = item.TournamentName,
                        TournamentId = item.TournamentId,
                        HomeParticipantId = item.HomeParticipantId,
                        AwayParticipantId = item.AwayParticipantId,
                        Status = item.Status,
                        ScheduledTime = item.ScheduledStartTime,
                        UserNickname = (isHome ? item.HomeNickname : item.AwayNickname) ?? "Unknown",
                        OpponentName = (isHome ? item.AwayUsername : item.HomeUsername) ?? "Unknown",
                        OpponentNickname = (isHome ? item.AwayNickname : item.HomeNickname) ?? "Unknown",
                        OpponentAvatarUrl = isHome ? item.AwayAvatarUrl : item.HomeAvatarUrl
                    });
                    continue;
                }

                // --- TEAM (sub-match of a team match) ---
                if (!item.TeamMatchId.HasValue) continue;

                bool isHomeTeam = item.HomeParticipantId.HasValue
                    && userTeamParticipantIds.Contains(item.HomeParticipantId.Value);

                var myParticipantId = isHomeTeam ? item.HomeParticipantId!.Value : item.AwayParticipantId!.Value;
                var opponentParticipantId = isHomeTeam ? item.AwayParticipantId!.Value : item.HomeParticipantId!.Value;

                if (!participantToTeamMap.TryGetValue(myParticipantId, out var myTeamId)) continue;
                if (!teamMembersMap.TryGetValue(myTeamId, out var myMembers) || myMembers.Count == 0) continue;

                int myIndex = myMembers.FindIndex(m => m.UserId == userId);
                if (myIndex < 0) continue;

                int teamSize = Math.Max(myMembers.Count, 1);
                if ((item.MatchOrder ?? 0) % teamSize != myIndex) continue;

                participantToTeamMap.TryGetValue(opponentParticipantId, out var opponentTeamId);
                List<TeamMemberInfo>? opponentMembers = opponentTeamId != default
                    ? teamMembersMap.GetValueOrDefault(opponentTeamId)
                    : null;

                var opponentMember = opponentMembers != null && myIndex < opponentMembers.Count
                    ? opponentMembers[myIndex]
                    : opponentMembers?.FirstOrDefault();

                result.Add(new MatchOverviewDto
                {
                    Id = item.Id!.Value,
                    HubName = item.HubName,
                    TournamentName = item.TournamentName,
                    TournamentId = item.TournamentId,
                    HomeParticipantId = item.HomeParticipantId,
                    AwayParticipantId = item.AwayParticipantId,
                    Status = item.Status,
                    ScheduledTime = item.ScheduledStartTime,
                    UserNickname = myMembers[myIndex].Nickname ?? "Unknown",
                    OpponentName = opponentMember?.Username ?? "Unknown",
                    OpponentNickname = opponentMember?.Nickname ?? "Unknown",
                    OpponentAvatarUrl = opponentMember?.AvatarUrl
                });
            }

            return result;
        }

        private sealed record TeamMemberInfo(Guid UserId, string? Username, string? Nickname, string? AvatarUrl);

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
                    OpponentAvatarUrl = m.HomeParticipant!.UserId == userId
                        ? m.AwayParticipant!.User!.AvatarUrl
                        : m.HomeParticipant!.User!.AvatarUrl,
                    OpponentScore = m.HomeParticipant!.UserId == userId
                        ? m.AwayUserScore
                        : m.HomeUserScore,
                    UserScore = m.HomeParticipant!.UserId == userId
                        ? m.HomeUserScore
                        : m.AwayUserScore,
                    UserAvatarUrl = m.HomeParticipant!.UserId == userId
                        ? m.HomeParticipant!.User!.AvatarUrl
                        : m.AwayParticipant!.User!.AvatarUrl,
                    Username = m.HomeParticipant!.UserId == userId
                        ? m.HomeParticipant!.User!.Username
                        : m.AwayParticipant!.User!.Username,
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

        private static List<TournamentTeamMemberEntity> GetSortedMembers(
          IEnumerable<TournamentTeamMemberEntity>? members)
        {
            return members?
                .Where(m => m.UserId.HasValue)
                .OrderBy(m => m.JoinedAt)
                .ThenBy(m => m.Id)
                .ToList() ?? [];
        }

        private static MatchOverviewDto MapToOverviewDto(
            MatchEntity match,
            string? userNickname,
            string? opponentName,
            string? opponentNickname,
            string? opponentAvatarUrl)
        {
            return new MatchOverviewDto
            {
                Id = match.Id!.Value,
                HubName = match.Tournament!.Hub!.Name,
                TournamentName = match.Tournament.Name,
                TournamentId = match.TournamentId,
                HomeParticipantId = match.HomeParticipantId,
                AwayParticipantId = match.AwayParticipantId,
                Status = match.Status,
                ScheduledTime = match.ScheduledStartTime,
                UserNickname = userNickname ?? "Unknown",
                OpponentName = opponentName ?? "Unknown",
                OpponentNickname = opponentNickname ?? "Unknown",
                OpponentAvatarUrl = opponentAvatarUrl
            };
        }
    }
}