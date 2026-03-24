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

            // 1. DOHVATAMO SVE TIMOVE KORISNIKA (Najsigurniji način za EF Core)
            var userTeamIds = await this.ContextBase.Set<TournamentTeamMemberEntity>()
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.TeamId.HasValue)
                .Select(m => m.TeamId!.Value)
                .ToListAsync();

            // Nalazimo Participant ID-jeve za te timove
            var teamParticipantIds = new List<Guid>();
            if (userTeamIds.Any())
            {
                teamParticipantIds = await this.ContextBase.Set<TournamentParticipantEntity>()
                    .AsNoTracking()
                    .Where(p => p.TeamId.HasValue && userTeamIds.Contains(p.TeamId!.Value))
                    .Select(p => p.Id!.Value)
                    .ToListAsync();
            }

            // 2. GLAVNI UPIT
            var matches = await this.BaseDbSet()
                .AsNoTrackingWithIdentityResolution()
                .Where(x =>
                    x.Tournament!.Status == TournamentStatus.InProgress &&
                    (x.Status == MatchStatus.Pending || x.Status == MatchStatus.Scheduled) &&
                    // PRIVREMENO ZAKOMENTARISANO: Otkomentariši ako moraš da proveravaš vreme runde!
                    // (x.RoundOpenAt == null || x.RoundOpenAt <= now) &&
                    // (x.RoundDeadline == null || x.RoundDeadline >= now) &&
                    (
                        // SOLO MEČEVI
                        (x.TeamMatchId == null && x.HomeParticipantId != null && x.AwayParticipantId != null &&
                            (x.HomeParticipant!.UserId == userId || x.AwayParticipant!.UserId == userId))
                        ||
                        // TIMSKI MEČEVI (Proveravamo da li se ID ucesnika poklapa sa listom nasih timova)
                        (x.TeamMatchId != null && x.TeamMatch != null &&
                            (teamParticipantIds.Contains(x.TeamMatch.HomeTeamParticipantId ?? Guid.Empty) ||
                             teamParticipantIds.Contains(x.TeamMatch.AwayTeamParticipantId ?? Guid.Empty)))
                    )
                )
                // Includes
                .Include(x => x.Tournament).ThenInclude(t => t!.Hub)
                .Include(x => x.HomeParticipant).ThenInclude(p => p!.User)
                .Include(x => x.AwayParticipant).ThenInclude(p => p!.User)
                .Include(x => x.TeamMatch).ThenInclude(tm => tm!.HomeTeamParticipant).ThenInclude(p => p!.Team).ThenInclude(t => t!.Members).ThenInclude(m => m.User)
                .Include(x => x.TeamMatch).ThenInclude(tm => tm!.AwayTeamParticipant).ThenInclude(p => p!.Team).ThenInclude(t => t!.Members).ThenInclude(m => m.User)
                .Include(x => x.TeamMatch).ThenInclude(tm => tm!.SubMatches) // Da bismo izvukli Index
                .OrderByDescending(x => x.ScheduledStartTime)
                .ToListAsync();

            var result = new List<MatchOverviewDto>();

            // 3. SPAJANJE IGRAČA SA MEČEVIMA
            foreach (var match in matches)
            {
                // --- SOLO MEČEVI ---
                if (match.TeamMatchId == null)
                {
                    bool isHome = match.HomeParticipant!.UserId == userId;
                    var me = isHome ? match.HomeParticipant.User : match.AwayParticipant!.User;
                    var opponent = isHome ? match.AwayParticipant!.User : match.HomeParticipant.User;

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
                    continue;
                }

                // --- TIMSKI SUB-MEČEVI ---
                var teamMatch = match.TeamMatch!;
                var homeTeam = teamMatch.HomeTeamParticipant?.Team;
                var awayTeam = teamMatch.AwayTeamParticipant?.Team;

                if (homeTeam == null || awayTeam == null) continue;

                var homeTeamMembers = homeTeam.Members.Where(m => m.UserId.HasValue).OrderBy(m => m.JoinedAt).ThenBy(m => m.Id).ToList();
                var awayTeamMembers = awayTeam.Members.Where(m => m.UserId.HasValue).OrderBy(m => m.JoinedAt).ThenBy(m => m.Id).ToList();

                // Nalazimo redni broj sub-meča
                var sortedSubMatches = teamMatch.SubMatches.OrderBy(s => s.MatchOrder).ToList();
                int subMatchIndex = sortedSubMatches.FindIndex(s => s.Id == match.Id);

                if (subMatchIndex < 0) continue;

                int baseTieBreakOrder = (teamMatch.MatchOrder ?? 0) + 1000;
                bool isTieBreakMatch = (match.MatchOrder ?? 0) >= baseTieBreakOrder;

                // Nalazimo igrače (ili iz baze ili po Indexu)
                Guid? resolvedHomeUserId = match.HomeParticipant?.UserId;
                if (resolvedHomeUserId == null)
                {
                    resolvedHomeUserId = isTieBreakMatch
                        ? teamMatch.HomeTeamRepresentativeUserId
                        : (subMatchIndex < homeTeamMembers.Count ? homeTeamMembers[subMatchIndex].UserId : null);
                }

                Guid? resolvedAwayUserId = match.AwayParticipant?.UserId;
                if (resolvedAwayUserId == null)
                {
                    resolvedAwayUserId = isTieBreakMatch
                        ? teamMatch.AwayTeamRepresentativeUserId
                        : (subMatchIndex < awayTeamMembers.Count ? awayTeamMembers[subMatchIndex].UserId : null);
                }

                // Ako ti ne igraš ovaj sub-meč (npr. ti si drugi u timu, a ovo je prvi meč), preskačemo!
                if (resolvedHomeUserId != userId && resolvedAwayUserId != userId) continue;

                bool iAmHome = resolvedHomeUserId == userId;
                var myMember = iAmHome ? homeTeamMembers.FirstOrDefault(m => m.UserId == resolvedHomeUserId) : awayTeamMembers.FirstOrDefault(m => m.UserId == resolvedAwayUserId);
                var opponentMember = iAmHome ? awayTeamMembers.FirstOrDefault(m => m.UserId == resolvedAwayUserId) : homeTeamMembers.FirstOrDefault(m => m.UserId == resolvedHomeUserId);

                result.Add(new MatchOverviewDto
                {
                    Id = match.Id!.Value,
                    HubName = match.Tournament!.Hub!.Name,
                    TournamentName = match.Tournament.Name,
                    TournamentId = match.TournamentId,
                    Status = match.Status,
                    ScheduledTime = match.ScheduledStartTime,
                    UserNickname = myMember?.User?.Nickname ?? myMember?.User?.Username ?? "Unknown",
                    OpponentName = opponentMember?.User?.Username ?? "Unknown",
                    OpponentNickname = opponentMember?.User?.Nickname ?? opponentMember?.User?.Username ?? "Unknown",
                    OpponentAvatarUrl = opponentMember?.User?.AvatarUrl
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