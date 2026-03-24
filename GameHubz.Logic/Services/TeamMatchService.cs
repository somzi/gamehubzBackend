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
                var homeRepParticipant = await FindParticipantByUserId(teamMatch.TournamentId, teamMatch.HomeTeamRepresentativeUserId.Value);
                var awayRepParticipant = await FindParticipantByUserId(teamMatch.TournamentId, teamMatch.AwayTeamRepresentativeUserId.Value);

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
                    HomeParticipantId = homeRepParticipant?.Id,
                    AwayParticipantId = awayRepParticipant?.Id,
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
            var teamMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(teamMatchId);
            if (teamMatch == null) throw new Exception("Team match not found.");

            return new TieBreakStatusDto
            {
                TeamMatchId = teamMatch.Id!.Value,
                Status = teamMatch.Status.ToString(),
                HomeRepresentative = teamMatch.HomeTeamRepresentativeUserId.HasValue
                    ? await GetUserAsTeamMember(teamMatch.HomeTeamRepresentativeUserId.Value)
                    : null,
                AwayRepresentative = teamMatch.AwayTeamRepresentativeUserId.HasValue
                    ? await GetUserAsTeamMember(teamMatch.AwayTeamRepresentativeUserId.Value)
                    : null
            };
        }

        public async Task<TeamMatchDetailsDto> GetTeamMatchDetails(Guid teamMatchId)
        {
            var teamMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(teamMatchId);
            if (teamMatch == null) throw new Exception("Team match not found.");

            var homeTeam = teamMatch.HomeTeamParticipant?.Team;
            var awayTeam = teamMatch.AwayTeamParticipant?.Team;

            var subMatches = teamMatch.SubMatches.OrderBy(s => s.MatchOrder).ToList();

            int homeWins = 0, awayWins = 0, homeTotalScore = 0, awayTotalScore = 0;

            foreach (var sm in subMatches)
            {
                if (sm.Status == MatchStatus.Completed && sm.WinnerParticipantId.HasValue)
                {
                    if (sm.WinnerParticipantId == teamMatch.HomeTeamParticipantId)
                        homeWins++;
                    else if (sm.WinnerParticipantId == teamMatch.AwayTeamParticipantId)
                        awayWins++;
                }
                homeTotalScore += sm.HomeUserScore ?? 0;
                awayTotalScore += sm.AwayUserScore ?? 0;
            }

            var homeTeamMembers = homeTeam?.Members?
                 .Where(m => m.UserId.HasValue)
                 .Select(m => new TeamMemberDto
                 {
                     UserId = m.UserId!.Value,
                     Username = m.User?.Username ?? "Unknown",
                     AvatarUrl = m.User?.AvatarUrl
                 })
                 .ToList() ?? new List<TeamMemberDto>();

            var awayTeamMembers = awayTeam?.Members?
                .Where(m => m.UserId.HasValue)
                .Select(m => new TeamMemberDto
                {
                    UserId = m.UserId!.Value,
                    Username = m.User?.Username ?? "Unknown",
                    AvatarUrl = m.User?.AvatarUrl
                })
                .ToList() ?? new List<TeamMemberDto>();

            TeamMemberDto? homeRepresentative = teamMatch.HomeTeamRepresentativeUserId.HasValue
                ? await GetUserAsTeamMember(teamMatch.HomeTeamRepresentativeUserId.Value)
                : null;

            TeamMemberDto? awayRepresentative = teamMatch.AwayTeamRepresentativeUserId.HasValue
                ? await GetUserAsTeamMember(teamMatch.AwayTeamRepresentativeUserId.Value)
                : null;

            int baseTieBreakOrder = (teamMatch.MatchOrder ?? 0) + 1000;

            var subMatchDtos = subMatches.Select(sm =>
            {
                bool isTieBreakMatch = (sm.MatchOrder ?? 0) >= baseTieBreakOrder;

                // Prioritize explicit HomeUserId/AwayUserId, fall back to Participant.UserId
                Guid? homeUserId = sm.HomeUserId ?? sm.HomeParticipant?.UserId;
                Guid? awayUserId = sm.AwayUserId ?? sm.AwayParticipant?.UserId;

                TeamMemberDto? homePlayer = null;
                if (sm.HomeUser != null)
                    homePlayer = new TeamMemberDto { UserId = sm.HomeUser.Id!.Value, Username = sm.HomeUser.Username };
                else if (sm.HomeParticipant?.User != null)
                    homePlayer = new TeamMemberDto { UserId = sm.HomeParticipant.UserId!.Value, Username = sm.HomeParticipant.User.Username };
                else if (homeUserId.HasValue)
                    homePlayer = homeTeamMembers.FirstOrDefault(m => m.UserId == homeUserId.Value);

                TeamMemberDto? awayPlayer = null;
                if (sm.AwayUser != null)
                    awayPlayer = new TeamMemberDto { UserId = sm.AwayUser.Id!.Value, Username = sm.AwayUser.Username };
                else if (sm.AwayParticipant?.User != null)
                    awayPlayer = new TeamMemberDto { UserId = sm.AwayParticipant.UserId!.Value, Username = sm.AwayParticipant.User.Username };
                else if (awayUserId.HasValue)
                    awayPlayer = awayTeamMembers.FirstOrDefault(m => m.UserId == awayUserId.Value);

                Guid? winnerUserId = sm.WinnerParticipant?.UserId;
                if (!winnerUserId.HasValue && sm.Status == MatchStatus.Completed)
                {
                    if ((sm.HomeUserScore ?? 0) > (sm.AwayUserScore ?? 0))
                        winnerUserId = homePlayer?.UserId;
                    else if ((sm.AwayUserScore ?? 0) > (sm.HomeUserScore ?? 0))
                        winnerUserId = awayPlayer?.UserId;
                }

                return new TeamSubMatchDto
                {
                    MatchId = sm.Id!.Value,
                    HomePlayer = homePlayer,
                    AwayPlayer = awayPlayer,
                    HomeScore = sm.HomeUserScore,
                    AwayScore = sm.AwayUserScore,
                    Status = sm.Status,
                    WinnerUserId = winnerUserId,
                    IsTieBreakMatch = isTieBreakMatch
                };
            }).ToList();

            return new TeamMatchDetailsDto
            {
                TeamMatchId = teamMatch.Id!.Value,
                Status = teamMatch.Status,
                WinnerTeamParticipantId = teamMatch.WinnerTeamParticipantId,
                HomeTeam = homeTeam == null ? null : new TeamMatchTeamInfoDto
                {
                    TeamId = homeTeam.Id!.Value,
                    TeamName = homeTeam.TeamName,
                    Members = homeTeamMembers
                },
                AwayTeam = awayTeam == null ? null : new TeamMatchTeamInfoDto
                {
                    TeamId = awayTeam.Id!.Value,
                    TeamName = awayTeam.TeamName,
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
                    IsRequired = teamMatch.Status == TeamMatchStatus.TieBreakRequired,
                    HomeRepresentative = homeRepresentative,
                    AwayRepresentative = awayRepresentative
                }
            };
        }

        private async Task<TournamentParticipantEntity?> FindParticipantByUserId(Guid tournamentId, Guid userId)
        {
            var participant = await this.AppUnitOfWork.TournamentParticipantRepository.GetUserByTournamentId(tournamentId, userId);
            return participant;
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