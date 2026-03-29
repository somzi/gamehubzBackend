using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;

namespace GameHubz.Logic.Services
{
    public class TeamMatchService : AppBaseService
    {
        private readonly ICacheService cacheService;

        public TeamMatchService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            ICacheService cacheService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.cacheService = cacheService;
        }

        public async Task<SubmitRepresentativeResponse> SubmitRepresentative(Guid teamMatchId, SubmitRepresentativeRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var teamMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(teamMatchId);
            if (teamMatch == null) throw new Exception("Team match not found.");

            if (teamMatch.Status != TeamMatchStatus.TieBreakRequired)
                throw new Exception("Tie-break is not required for this match.");

            var homeTeam = teamMatch.HomeTeamParticipant?.Team;
            var awayTeam = teamMatch.AwayTeamParticipant?.Team;

            bool isHomeCaptain = homeTeam?.CaptainUserId == user.UserId;
            bool isAwayCaptain = awayTeam?.CaptainUserId == user.UserId;

            if (!isHomeCaptain && !isAwayCaptain)
                throw new Exception("Only a team captain can submit a representative.");

            if (isHomeCaptain)
            {
                bool isMemberOfHomeTeam = homeTeam!.Members.Any(m => m.UserId == request.UserId);
                if (!isMemberOfHomeTeam) throw new Exception("Selected user is not a member of your team.");
                teamMatch.HomeTeamRepresentativeUserId = request.UserId;
            }
            else
            {
                bool isMemberOfAwayTeam = awayTeam!.Members.Any(m => m.UserId == request.UserId);
                if (!isMemberOfAwayTeam) throw new Exception("Selected user is not a member of your team.");
                teamMatch.AwayTeamRepresentativeUserId = request.UserId;
            }

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
            await cacheService.RemoveAsync($"bracket:{teamMatch.TournamentId}");

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
            if (projection == null) throw new Exception("Team match not found.");

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
            if (projection == null) throw new Exception("Team match not found.");

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
                homeTotalScore += sm.HomeUserScore ?? 0;
                awayTotalScore += sm.AwayUserScore ?? 0;
            }

            int baseTieBreakOrder = (projection.MatchOrder ?? 0) + 1000;

            var subMatchDtos = projection.SubMatches.Select(sm =>
            {
                bool isTieBreakMatch = (sm.MatchOrder ?? 0) >= baseTieBreakOrder;

                TeamMemberDto? homePlayer = null;
                if (sm.HomeUserId.HasValue && sm.HomeUsername != null)
                    homePlayer = new TeamMemberDto { UserId = sm.HomeUserId.Value, Username = sm.HomeUsername, AvatarUrl = sm.HomeAvatarUrl };
                else if (sm.HomeUserId.HasValue)
                    homePlayer = homeTeamMembers.FirstOrDefault(m => m.UserId == sm.HomeUserId.Value);

                TeamMemberDto? awayPlayer = null;
                if (sm.AwayUserId.HasValue && sm.AwayUsername != null)
                    awayPlayer = new TeamMemberDto { UserId = sm.AwayUserId.Value, Username = sm.AwayUsername, AvatarUrl = sm.AwayAvatarUrl };
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
                    Evidences = sm.Evidences
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
                }
            };
        }

        private async Task<TeamMemberDto> GetUserAsTeamMember(Guid userId)
        {
            var user = await this.AppUnitOfWork.UserRepository.GetById(userId);
            return new TeamMemberDto
            {
                UserId = userId,
                Username = user?.Username ?? "Unknown"
            };
        }
    }
}