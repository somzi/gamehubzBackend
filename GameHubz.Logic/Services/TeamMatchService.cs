using GameHubz.DataModels.Consts;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;

namespace GameHubz.Logic.Services
{
    public class TeamMatchService : AppBaseService
    {
        private readonly ICacheService cacheService;
        private readonly TournamentAuthorizationService tournamentAuth;

        public TeamMatchService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            ICacheService cacheService,
            TournamentAuthorizationService tournamentAuth)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.cacheService = cacheService;
            this.tournamentAuth = tournamentAuth;
        }

        public async Task<SubmitRepresentativeResponse> SubmitRepresentative(Guid teamMatchId, SubmitRepresentativeRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var teamMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(teamMatchId);
            if (teamMatch == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamMatchNotFound"]);

            if (teamMatch.Status != TeamMatchStatus.TieBreakRequired)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TieBreakNotRequired"]);

            var homeTeam = teamMatch.HomeTeamParticipant?.Team;
            var awayTeam = teamMatch.AwayTeamParticipant?.Team;

            bool isHomeCaptain = homeTeam?.CaptainUserId == user.UserId;
            bool isAwayCaptain = awayTeam?.CaptainUserId == user.UserId;

            bool nomineeOnHome = homeTeam?.Members.Any(m => m.UserId == request.UserId) == true;
            bool nomineeOnAway = awayTeam?.Members.Any(m => m.UserId == request.UserId) == true;

            // A captain nominating off their own roster is the common path and stays exactly as it
            // was — no authorization round-trip. Everything else (a tournament manager standing in
            // for an inactive captain, or a captain reaching across to the other team) requires
            // manager rights: platform admin, hub owner or hub admin of the owning hub.
            bool captainOwnRoster = (isHomeCaptain && nomineeOnHome) || (isAwayCaptain && nomineeOnAway);
            bool isManager = !captainOwnRoster
                && await this.tournamentAuth.CanManageTournamentAsync(teamMatch.TournamentId, user);

            if (!captainOwnRoster && !isManager)
            {
                if (isHomeCaptain || isAwayCaptain)
                    throw new BusinessRuleException(this.LocalizationService["BusinessRule.UserNotInYourTeam"]);

                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyCaptainOrManagerRepresentative"]);
            }

            // The side follows the nominee's roster, not the caller's: a captain can only land on
            // their own team anyway, while a manager sets whichever side the nominee plays for.
            if (nomineeOnHome)
                teamMatch.HomeTeamRepresentativeUserId = request.UserId;
            else if (nomineeOnAway)
                teamMatch.AwayTeamRepresentativeUserId = request.UserId;
            else
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.UserNotInEitherTeam"]);

            await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(teamMatch, this.UserContextReader);

            Guid? tieBreakMatchId = null;

            if (teamMatch.HomeTeamRepresentativeUserId.HasValue && teamMatch.AwayTeamRepresentativeUserId.HasValue)
            {
                var tieBreakMatch = new MatchEntity
                {
                    Id = Guid.NewGuid(),
                    TournamentId = teamMatch.TournamentId,
                    TournamentStageId = teamMatch.TournamentStageId,
                    RoundNumber = teamMatch.RoundNumber,
                    Stage = MatchStage.GroupStage,
                    MatchOrder = (teamMatch.MatchOrder ?? 0) + 1000,
                    Status = MatchStatus.Pending,
                    IsUpperBracket = true,
                    // The decider belongs to the same round as the games that produced the tie, so
                    // it answers to their deadline. Without this it is created with none — no
                    // deadline in the app and no reminder pushes.
                    RoundDeadline = teamMatch.SubMatches.Max(sm => sm.RoundDeadline),
                    TeamMatchId = teamMatch.Id,
                    HomeParticipantId = teamMatch.HomeTeamParticipantId,
                    AwayParticipantId = teamMatch.AwayTeamParticipantId,
                    HomeUserId = teamMatch.HomeTeamRepresentativeUserId,
                    AwayUserId = teamMatch.AwayTeamRepresentativeUserId
                };

                await this.AppUnitOfWork.MatchRepository.AddEntity(tieBreakMatch, this.UserContextReader);
                tieBreakMatchId = tieBreakMatch.Id;

                teamMatch.Status = TeamMatchStatus.Pending;
                await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(teamMatch, this.UserContextReader);
            }

            await this.SaveAsync();
            await cacheService.RemoveByPatternAsync($"bracket:{teamMatch.TournamentId}:*");
            await cacheService.RemoveByPatternAsync($"bracket:v3:{teamMatch.TournamentId}:*");
            await cacheService.RemoveAsync($"league_standings:{teamMatch.TournamentId}");

            return new SubmitRepresentativeResponse
            {
                TeamMatchId = teamMatch.Id!.Value,
                Status = teamMatch.Status.ToString(),
                HomeRepresentative = teamMatch.HomeTeamRepresentativeUserId.HasValue
                    ? await GetUserAsTeamMember(teamMatch.HomeTeamRepresentativeUserId.Value)
                    : null,
                AwayRepresentative = teamMatch.AwayTeamRepresentativeUserId.HasValue
                    ? await GetUserAsTeamMember(teamMatch.AwayTeamRepresentativeUserId.Value)
                    : null,
                TieBreakMatchId = tieBreakMatchId
            };
        }

        public async Task<TieBreakStatusDto> GetTieBreakStatus(Guid teamMatchId)
        {
            var projection = await this.AppUnitOfWork.TeamMatchRepository.GetTieBreakProjection(teamMatchId);
            if (projection == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamMatchNotFound"]);

            return new TieBreakStatusDto
            {
                TeamMatchId = projection.TeamMatchId,
                Status = projection.Status.ToString(),
                HomeRepresentative = projection.HomeTeamRepresentativeUserId.HasValue
                    ? new TeamMemberDto
                    {
                        UserId = projection.HomeTeamRepresentativeUserId.Value,
                        Username = projection.HomeRepresentativeUsername ?? "Unknown"
                    }
                    : null,
                AwayRepresentative = projection.AwayTeamRepresentativeUserId.HasValue
                    ? new TeamMemberDto
                    {
                        UserId = projection.AwayTeamRepresentativeUserId.Value,
                        Username = projection.AwayRepresentativeUsername ?? "Unknown"
                    }
                    : null
            };
        }

        public async Task<TeamMatchDetailsDto> GetTeamMatchDetails(Guid teamMatchId)
        {
            var projection = await this.AppUnitOfWork.TeamMatchRepository.GetDetailsProjection(teamMatchId);
            if (projection == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamMatchNotFound"]);

            var homeTeamMembers = projection.HomeTeam?.Members ?? [];
            var awayTeamMembers = projection.AwayTeam?.Members ?? [];

            int homeWins = 0, awayWins = 0, homeTotalScore = 0, awayTotalScore = 0;

            foreach (var sm in projection.SubMatches)
            {
                if (sm.Status == MatchStatus.Completed && sm.WinnerParticipantId.HasValue)
                {
                    if (sm.WinnerParticipantId == projection.HomeTeamParticipantId)
                        homeWins++;
                    else if (sm.WinnerParticipantId == projection.AwayTeamParticipantId)
                        awayWins++;
                }
                // Real goals across the series, not the headline: under MatchWins a Bo3 won 2–1
                // reports a headline of 2, which would turn the tie's aggregate into a second win
                // tally. Null on pre-series sub-matches, where the headline IS the score.
                homeTotalScore += sm.HomeGoalsTotal ?? sm.HomeUserScore ?? 0;
                awayTotalScore += sm.AwayGoalsTotal ?? sm.AwayUserScore ?? 0;
            }

            int baseTieBreakOrder = (projection.MatchOrder ?? 0) + 1000;

            // Ready check, resolved once for the whole tie: every game of it runs under the same
            // tournament setting and the same grace window.
            int checkInGrace = MatchCheckInRules.ResolveGraceMinutes(projection.CheckInGraceMinutes);
            bool runsCheckIn(SubMatchProjection sm) =>
                projection.RequireMatchCheckIn
                && sm.ScheduledStartTime.HasValue
                && sm.Status == MatchStatus.Scheduled
                && sm.CheckInResolvedOn == null;

            var subMatchDtos = projection.SubMatches.Select(sm =>
            {
                bool isTieBreakMatch = (sm.MatchOrder ?? 0) >= baseTieBreakOrder;

                TeamMemberDto? homePlayer = null;
                if (sm.HomeUserId.HasValue && sm.HomeUsername != null)
                    homePlayer = new TeamMemberDto { UserId = sm.HomeUserId.Value, Username = sm.HomeUsername, Nickname = sm.HomeNickname, AvatarUrl = sm.HomeAvatarUrl };
                else if (sm.HomeUserId.HasValue)
                    homePlayer = homeTeamMembers.FirstOrDefault(m => m.UserId == sm.HomeUserId.Value);

                TeamMemberDto? awayPlayer = null;
                if (sm.AwayUserId.HasValue && sm.AwayUsername != null)
                    awayPlayer = new TeamMemberDto { UserId = sm.AwayUserId.Value, Username = sm.AwayUsername, Nickname = sm.AwayNickname, AvatarUrl = sm.AwayAvatarUrl };
                else if (sm.AwayUserId.HasValue)
                    awayPlayer = awayTeamMembers.FirstOrDefault(m => m.UserId == sm.AwayUserId.Value);

                Guid? winnerUserId = null;
                if (sm.WinnerParticipantId.HasValue)
                {
                    if (sm.WinnerParticipantId == sm.HomeParticipantId)
                        winnerUserId = homePlayer?.UserId;
                    else if (sm.WinnerParticipantId == sm.AwayParticipantId)
                        winnerUserId = awayPlayer?.UserId;
                }
                else if (sm.Status == MatchStatus.Completed)
                {
                    if ((sm.HomeUserScore ?? 0) > (sm.AwayUserScore ?? 0))
                        winnerUserId = homePlayer?.UserId;
                    else if ((sm.AwayUserScore ?? 0) > (sm.HomeUserScore ?? 0))
                        winnerUserId = awayPlayer?.UserId;
                }

                return new TeamSubMatchDto
                {
                    MatchId = sm.MatchId,
                    HomePlayer = homePlayer,
                    AwayPlayer = awayPlayer,
                    HomeScore = sm.HomeUserScore,
                    AwayScore = sm.AwayUserScore,
                    Status = sm.Status,
                    WinnerUserId = winnerUserId,
                    IsTieBreakMatch = isTieBreakMatch,
                    Evidences = sm.Evidences,
                    EvidenceItems = sm.EvidenceItems,
                    ProposedHomeScore = sm.ProposedHomeScore,
                    ProposedAwayScore = sm.ProposedAwayScore,
                    ProposedByUserId = sm.ProposedByUserId,
                    AdminHelpRequested = sm.AdminHelpRequested,
                    AdminHelpRequestedByUserId = sm.AdminHelpRequestedByUserId,
                    BestOf = sm.BestOf,
                    TiebreakBestOf = sm.TiebreakBestOf,
                    Games = DeserializeGames(sm.GamesJson),
                    ProposedGames = DeserializeGames(sm.ProposedGamesJson),
                    ScheduledStartTime = sm.ScheduledStartTime,
                    HomeCheckedInOn = sm.HomeCheckedInOn,
                    AwayCheckedInOn = sm.AwayCheckedInOn,
                    // Only a game that can still be turned up for gets a window: the check is on,
                    // a kick-off was agreed, and nothing has ruled on it yet.
                    CheckInOpensAt = runsCheckIn(sm) ? MatchCheckInRules.OpensAt(sm.ScheduledStartTime!.Value) : null,
                    CheckInDeadline = runsCheckIn(sm)
                        ? MatchCheckInRules.Deadline(
                            sm.ScheduledStartTime!.Value, sm.HomeCheckedInOn, sm.AwayCheckedInOn, checkInGrace)
                        : null,
                };
            }).ToList();

            TeamMemberDto? homeRepresentative = null;
            if (projection.HomeTeamRepresentativeUserId.HasValue)
            {
                homeRepresentative = homeTeamMembers.FirstOrDefault(m => m.UserId == projection.HomeTeamRepresentativeUserId.Value)
                    ?? await GetUserAsTeamMember(projection.HomeTeamRepresentativeUserId.Value);
            }

            TeamMemberDto? awayRepresentative = null;
            if (projection.AwayTeamRepresentativeUserId.HasValue)
            {
                awayRepresentative = awayTeamMembers.FirstOrDefault(m => m.UserId == projection.AwayTeamRepresentativeUserId.Value)
                    ?? await GetUserAsTeamMember(projection.AwayTeamRepresentativeUserId.Value);
            }

            return new TeamMatchDetailsDto
            {
                TeamMatchId = projection.TeamMatchId,
                Status = projection.Status,
                WinnerTeamParticipantId = projection.WinnerTeamParticipantId,
                HomeTeamParticipantId = projection.HomeTeamParticipantId,
                AwayTeamParticipantId = projection.AwayTeamParticipantId,
                WinCondition = projection.WinCondition,
                SeriesWinCondition = projection.SeriesWinCondition,
                HomeTeam = projection.HomeTeam == null ? null : new TeamMatchTeamInfoDto
                {
                    TeamId = projection.HomeTeam.TeamId,
                    TeamName = projection.HomeTeam.TeamName,
                    CaptainUserId = projection.HomeTeam.CaptainUserId,
                    Members = homeTeamMembers
                },
                AwayTeam = projection.AwayTeam == null ? null : new TeamMatchTeamInfoDto
                {
                    TeamId = projection.AwayTeam.TeamId,
                    TeamName = projection.AwayTeam.TeamName,
                    CaptainUserId = projection.AwayTeam.CaptainUserId,
                    Members = awayTeamMembers
                },
                SubMatches = subMatchDtos,
                AggregateScore = new TeamAggregateScoreDto
                {
                    HomeTeamWins = homeWins,
                    AwayTeamWins = awayWins,
                    HomeTeamTotalScore = homeTotalScore,
                    AwayTeamTotalScore = awayTotalScore
                },
                TieBreak = new TeamTieBreakInfoDto
                {
                    IsRequired = projection.Status == TeamMatchStatus.TieBreakRequired,
                    HomeRepresentative = homeRepresentative,
                    AwayRepresentative = awayRepresentative
                },
                RequireResultApproval = projection.RequireResultApproval,
                RequireMatchCheckIn = projection.RequireMatchCheckIn,
                CheckInGraceMinutes = projection.RequireMatchCheckIn ? checkInGrace : null
            };
        }

        // The projection carries the games as raw JSON (EF can't deserialize mid-query). A malformed
        // blob degrades to "no per-game breakdown" rather than failing the whole team-match screen.
        private static List<SeriesGame>? DeserializeGames(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return System.Text.Json.JsonSerializer.Deserialize<List<SeriesGame>>(json); }
            catch { return null; }
        }

        private async Task<TeamMemberDto> GetUserAsTeamMember(Guid userId)
        {
            var user = await this.AppUnitOfWork.UserRepository.GetById(userId);
            return new TeamMemberDto
            {
                UserId = userId,
                Username = user?.Username ?? "Unknown",
                Nickname = string.IsNullOrWhiteSpace(user?.Nickname) ? null : user!.Nickname
            };
        }
    }
}