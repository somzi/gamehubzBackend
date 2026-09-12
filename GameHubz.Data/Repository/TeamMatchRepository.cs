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

        public async Task<bool> TryClaimForProcessing(Guid teamMatchId)
        {
            int affected = await this.BaseDbSet()
                .Where(tm => tm.Id == teamMatchId && tm.Status == TeamMatchStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(tm => tm.Status, TeamMatchStatus.Processing));

            return affected > 0;
        }

        public async Task<List<TeamMatchEntity>> GetByStageId(Guid stageId)
        {
            return await this.BaseDbSet()
                .Where(tm => tm.TournamentStageId == stageId)
                .Include(tm => tm.SubMatches)
                .ToListAsync();
        }

        public async Task<List<TeamMatchEntity>> GetByTournamentId(Guid tournamentId)
        {
            return await this.BaseDbSet()
                .Where(tm => tm.TournamentId == tournamentId)
                .ToListAsync();
        }

        public async Task<TeamMatchDetailsProjection?> GetDetailsProjection(Guid teamMatchId)
        {
            return await this.BaseDbSet()
                .Where(tm => tm.Id == teamMatchId)
                .Select(tm => new TeamMatchDetailsProjection
                {
                    TeamMatchId = tm.Id!.Value,
                    Status = tm.Status,
                    WinnerTeamParticipantId = tm.WinnerTeamParticipantId,
                    HomeTeamParticipantId = tm.HomeTeamParticipantId,
                    AwayTeamParticipantId = tm.AwayTeamParticipantId,
                    HomeTeamRepresentativeUserId = tm.HomeTeamRepresentativeUserId,
                    AwayTeamRepresentativeUserId = tm.AwayTeamRepresentativeUserId,
                    MatchOrder = tm.MatchOrder,
                    RequireResultApproval = tm.Tournament!.RequireResultApproval,
                    RequireMatchCheckIn = tm.Tournament!.RequireMatchCheckIn,
                    CheckInGraceMinutes = tm.Tournament!.CheckInGraceMinutes,
                    WinCondition = tm.Tournament!.TeamWinCondition,
                    HomeTeam = tm.HomeTeamParticipant != null && tm.HomeTeamParticipant.Team != null
                        ? new TeamMatchTeamProjection
                        {
                            TeamId = tm.HomeTeamParticipant.Team.Id!.Value,
                            TeamName = tm.HomeTeamParticipant.Team.TeamName,
                            CaptainUserId = tm.HomeTeamParticipant.Team.CaptainUserId,
                            // Whole roster, bench included: the tie-break representative may be any
                            // squad member, and IsReserve lets the picker label who is benched.
                            Members = tm.HomeTeamParticipant.Team.Members
                                .Where(m => m.UserId.HasValue)
                                .Select(m => new TeamMemberDto
                                {
                                    UserId = m.UserId!.Value,
                                    Username = m.User != null ? m.User.Username : "Unknown",
                                    Nickname = m.User != null && !string.IsNullOrWhiteSpace(m.User.Nickname) ? m.User.Nickname : null,
                                    AvatarUrl = m.User != null ? m.User.AvatarUrl : null,
                                    // Ping context for the two players of a sub-match. Raw code
                                    // only — flag and name are catalog lookups on the DTO.
                                    Country = m.User != null ? m.User.Country : null,
                                    IsReserve = m.IsReserve
                                }).ToList()
                        }
                        : null,
                    AwayTeam = tm.AwayTeamParticipant != null && tm.AwayTeamParticipant.Team != null
                        ? new TeamMatchTeamProjection
                        {
                            TeamId = tm.AwayTeamParticipant.Team.Id!.Value,
                            TeamName = tm.AwayTeamParticipant.Team.TeamName,
                            CaptainUserId = tm.AwayTeamParticipant.Team.CaptainUserId,
                            Members = tm.AwayTeamParticipant.Team.Members
                                .Where(m => m.UserId.HasValue)
                                .Select(m => new TeamMemberDto
                                {
                                    UserId = m.UserId!.Value,
                                    Username = m.User != null ? m.User.Username : "Unknown",
                                    Nickname = m.User != null && !string.IsNullOrWhiteSpace(m.User.Nickname) ? m.User.Nickname : null,
                                    AvatarUrl = m.User != null ? m.User.AvatarUrl : null,
                                    // Ping context for the two players of a sub-match. Raw code
                                    // only — flag and name are catalog lookups on the DTO.
                                    Country = m.User != null ? m.User.Country : null,
                                    IsReserve = m.IsReserve
                                }).ToList()
                        }
                        : null,
                    SubMatches = tm.SubMatches.OrderBy(sm => sm.MatchOrder).Select(sm => new SubMatchProjection
                    {
                        MatchId = sm.Id!.Value,
                        MatchOrder = sm.MatchOrder,
                        Status = sm.Status,
                        HomeUserScore = sm.HomeUserScore,
                        AwayUserScore = sm.AwayUserScore,
                        WinnerParticipantId = sm.WinnerParticipantId,
                        ScheduledStartTime = sm.ScheduledStartTime,
                        HomeCheckedInOn = sm.HomeCheckedInOn,
                        AwayCheckedInOn = sm.AwayCheckedInOn,
                        CheckInResolvedOn = sm.CheckInResolvedOn,
                        HomeParticipantId = sm.HomeParticipantId,
                        AwayParticipantId = sm.AwayParticipantId,
                        HomeUserId = sm.HomeUserId ?? (sm.HomeParticipant != null ? sm.HomeParticipant.UserId : null),
                        AwayUserId = sm.AwayUserId ?? (sm.AwayParticipant != null ? sm.AwayParticipant.UserId : null),
                        HomeUsername = sm.HomeUser != null
                            ? sm.HomeUser.Username
                            : (sm.HomeParticipant != null && sm.HomeParticipant.User != null
                                ? sm.HomeParticipant.User.Username
                                : null),
                        AwayUsername = sm.AwayUser != null
                            ? sm.AwayUser.Username
                            : (sm.AwayParticipant != null && sm.AwayParticipant.User != null
                                ? sm.AwayParticipant.User.Username
                                : null),
                        // Blank nicknames (entity default "") come back as null so the client can
                        // just check for presence before rendering the in-game name.
                        HomeNickname = sm.HomeUser != null
                            ? (string.IsNullOrWhiteSpace(sm.HomeUser.Nickname) ? null : sm.HomeUser.Nickname)
                            : (sm.HomeParticipant != null && sm.HomeParticipant.User != null
                                ? (string.IsNullOrWhiteSpace(sm.HomeParticipant.User.Nickname) ? null : sm.HomeParticipant.User.Nickname)
                                : null),
                        AwayNickname = sm.AwayUser != null
                            ? (string.IsNullOrWhiteSpace(sm.AwayUser.Nickname) ? null : sm.AwayUser.Nickname)
                            : (sm.AwayParticipant != null && sm.AwayParticipant.User != null
                                ? (string.IsNullOrWhiteSpace(sm.AwayParticipant.User.Nickname) ? null : sm.AwayParticipant.User.Nickname)
                                : null),
                        HomeAvatarUrl = sm.HomeUser != null
                            ? sm.HomeUser.AvatarUrl
                            : (sm.HomeParticipant != null && sm.HomeParticipant.User != null
                                ? sm.HomeParticipant.User.AvatarUrl
                                : null),
                        AwayAvatarUrl = sm.AwayUser != null
                            ? sm.AwayUser.AvatarUrl
                            : (sm.AwayParticipant != null && sm.AwayParticipant.User != null
                                ? sm.AwayParticipant.User.AvatarUrl
                                : null),
                        Evidences = sm.MatchEvidences != null
                            ? sm.MatchEvidences.Select(e => e.Url!).ToList()
                            : new List<string>(),
                        EvidenceItems = sm.MatchEvidences != null
                            ? sm.MatchEvidences.Select(e => new MatchEvidenceItemDto { Url = e.Url!, MediaType = e.MediaType }).ToList()
                            : new List<MatchEvidenceItemDto>(),
                        ProposedHomeScore = sm.ProposedHomeScore,
                        ProposedAwayScore = sm.ProposedAwayScore,
                        ProposedByUserId = sm.ProposedByUserId,
                        AdminHelpRequested = sm.AdminHelpRequested,
                        AdminHelpRequestedByUserId = sm.AdminHelpRequestedByUserId,
                        // Each individual game of the tie is its own series. Resolved here (match
                        // override, else the tournament default) so the client can render the
                        // per-game entry form without a second lookup.
                        BestOf = sm.BestOf ?? (
                            tm.Tournament!.KnockoutBestOf != null
                            && (tm.Tournament.Format == TournamentFormat.GroupsThenSingleElimination
                                || tm.Tournament.Format == TournamentFormat.GroupsThenDoubleElimination
                                || tm.Tournament.Format == TournamentFormat.GroupStageWithKnockout
                                || tm.Tournament.Format == TournamentFormat.Swiss)
                            && tm.TournamentStage != null
                            && (tm.TournamentStage.Type == StageType.SingleEliminationBracket
                                || tm.TournamentStage.Type == StageType.DoubleEliminationWinnersBracket
                                || tm.TournamentStage.Type == StageType.DoubleEliminationLosersBracket
                                || tm.TournamentStage.Type == StageType.PlayIn)
                                ? tm.Tournament.KnockoutBestOf!.Value
                                : tm.Tournament.BestOf),
                        TiebreakBestOf = sm.TiebreakBestOf ?? tm.Tournament!.TiebreakBestOf,
                        GamesJson = sm.GamesJson,
                        ProposedGamesJson = sm.ProposedGamesJson,
                        HomeGoalsTotal = sm.HomeGoalsTotal,
                        AwayGoalsTotal = sm.AwayGoalsTotal
                    }).ToList(),
                    SeriesWinCondition = tm.Tournament!.SeriesWinCondition
                })
                .FirstOrDefaultAsync();
        }

        public async Task<TieBreakProjection?> GetTieBreakProjection(Guid teamMatchId)
        {
            return await this.BaseDbSet()
                .Where(tm => tm.Id == teamMatchId)
                .Select(tm => new TieBreakProjection
                {
                    TeamMatchId = tm.Id!.Value,
                    Status = tm.Status,
                    HomeTeamRepresentativeUserId = tm.HomeTeamRepresentativeUserId,
                    HomeRepresentativeUsername = tm.HomeTeamRepresentativeUserId.HasValue
                        ? ContextBase.Set<UserEntity>()
                            .Where(u => u.Id == tm.HomeTeamRepresentativeUserId)
                            .Select(u => u.Username)
                            .FirstOrDefault()
                        : null,
                    AwayTeamRepresentativeUserId = tm.AwayTeamRepresentativeUserId,
                    AwayRepresentativeUsername = tm.AwayTeamRepresentativeUserId.HasValue
                        ? ContextBase.Set<UserEntity>()
                            .Where(u => u.Id == tm.AwayTeamRepresentativeUserId)
                            .Select(u => u.Username)
                            .FirstOrDefault()
                        : null
                })
                .FirstOrDefaultAsync();
        }
    }
}