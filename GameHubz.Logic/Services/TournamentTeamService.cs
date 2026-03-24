using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class TournamentTeamService : AppBaseService
    {
        private readonly ICacheService cacheService;

        public TournamentTeamService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            ICacheService cacheService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.cacheService = cacheService;
        }

        public async Task<TeamDto> CreateTeam(CreateTeamRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(request.TournamentId);

            if (!tournament.IsTeamTournament)
                throw new Exception("This tournament is not a team tournament.");

            if (tournament.Status != TournamentStatus.RegistrationOpen)
                throw new Exception("Tournament registration is not open.");

            var alreadyInTeam = await this.AppUnitOfWork.TournamentTeamMemberRepository.ExistsInTournament(user.UserId, request.TournamentId);
            if (alreadyInTeam)
                throw new Exception("User is already in a team for this tournament.");

            var team = new TournamentTeamEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = request.TournamentId,
                TeamName = request.TeamName,
                CaptainUserId = user.UserId,
                CreatedOn = DateTime.UtcNow
            };

            await this.AppUnitOfWork.TournamentTeamRepository.AddEntity(team, this.UserContextReader);

            var member = new TournamentTeamMemberEntity
            {
                Id = Guid.NewGuid(),
                TeamId = team.Id,
                UserId = user.UserId,
                JoinedAt = DateTime.UtcNow
            };

            await this.AppUnitOfWork.TournamentTeamMemberRepository.AddEntity(member, this.UserContextReader);

            await this.SaveAsync();

            await InvalidateCache(request.TournamentId);

            return MapTeamsToDto(team, [member], tournament.TeamSize);
        }

        public async Task<TeamDto> RenameTeam(Guid teamId, RenameTeamRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new Exception("Team not found.");

            if (team.CaptainUserId != user.UserId)
                throw new Exception("Only the captain can rename the team.");

            team.TeamName = request.TeamName;
            await this.AppUnitOfWork.TournamentTeamRepository.UpdateEntity(team, this.UserContextReader);
            await this.SaveAsync();

            await InvalidateCache(team.TournamentId!.Value);

            return MapTeamsToDto(team, team.Members, team.Tournament?.TeamSize);
        }

        public async Task DeleteTeam(Guid teamId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new Exception("Team not found.");

            if (team.CaptainUserId != user.UserId)
                throw new Exception("Only the captain can delete the team.");

            foreach (var member in team.Members)
            {
                await this.AppUnitOfWork.TournamentTeamMemberRepository.SoftDeleteEntity(member, this.UserContextReader);
            }

            await this.AppUnitOfWork.TournamentTeamRepository.SoftDeleteEntity(team, this.UserContextReader);
            await this.SaveAsync();

            await InvalidateCache(team.TournamentId!.Value);
        }

        public async Task<TeamDto> JoinTeam(Guid teamId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new Exception("Team not found.");

            var tournament = team.Tournament ?? await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(team.TournamentId!.Value);

            if (!tournament.TeamSize.HasValue)
                throw new Exception("Tournament team size is not configured.");

            if (team.Members.Count >= tournament.TeamSize.Value)
                throw new Exception("Team is already full.");

            var alreadyInTeam = await this.AppUnitOfWork.TournamentTeamMemberRepository.ExistsInTournament(user.UserId, tournament.Id!.Value);
            if (alreadyInTeam)
                throw new Exception("User is already in a team for this tournament.");

            var member = new TournamentTeamMemberEntity
            {
                Id = Guid.NewGuid(),
                TeamId = team.Id,
                UserId = user.UserId,
                JoinedAt = DateTime.UtcNow
            };

            await this.AppUnitOfWork.TournamentTeamMemberRepository.AddEntity(member, this.UserContextReader);
            await this.SaveAsync();

            await InvalidateCache(tournament.Id!.Value);

            var members = await this.AppUnitOfWork.TournamentTeamMemberRepository.GetByTeamId(teamId);
            return MapTeamsToDto(team, members, tournament.TeamSize);
        }

        public async Task KickMember(Guid teamId, Guid userId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new Exception("Team not found.");

            if (team.CaptainUserId != user.UserId)
                throw new Exception("Only the captain can kick members.");

            if (userId == user.UserId)
                throw new Exception("Captain cannot kick themselves.");

            var member = team.Members.FirstOrDefault(m => m.UserId == userId);
            if (member == null) throw new Exception("User is not a member of this team.");

            await this.AppUnitOfWork.TournamentTeamMemberRepository.SoftDeleteEntity(member, this.UserContextReader);
            await this.SaveAsync();

            await InvalidateCache(team.TournamentId!.Value);
        }

        public async Task<List<TeamDto>> GetTeamsByTournament(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);
            var teams = await this.AppUnitOfWork.TournamentTeamRepository.GetByTournamentId(tournamentId);

            return teams.Select(t => MapTeamsToDto(t, t.Members, tournament.TeamSize)).ToList();
        }

        public async Task<List<TeamDto>> GetFinalTeamsByTournament(Guid tournamentId)
        {
            var teams = await this.AppUnitOfWork.TournamentTeamRepository.GetFinalByTournamentId(tournamentId);

            return teams.Select(t => MapTeamsToDto(t, t.Members, 0)).ToList();
        }

        public async Task<TeamDto> GetTeamByTournament(Guid tournamentId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetSingleByTournamentId(tournamentId, user.UserId);

            var teamDto = MapTeamToDto(team);

            if (team.Tournament!.TournamentRegistrations != null)
            {
                teamDto.IsAlreadyRegistred = team.Tournament!.TournamentRegistrations.Any(x => x.TeamId == team.Id
                && (x.Status == TournamentRegistrationStatus.Pending
                || x.Status == TournamentRegistrationStatus.Approved));
            }

            return teamDto;
        }

        public async Task LeaveTeam(Guid teamId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new Exception("Team not found.");

            var member = team.Members.FirstOrDefault(m => m.UserId == user.UserId);
            if (member == null) throw new Exception("User is not a member of this team.");

            await this.AppUnitOfWork.TournamentTeamMemberRepository.SoftDeleteEntity(member, this.UserContextReader);
            await this.SaveAsync();

            await InvalidateCache(team.TournamentId!.Value);
        }

        private async Task InvalidateCache(Guid tournamentId)
        {
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
        }

        private static TeamDto MapTeamsToDto(TournamentTeamEntity team, IEnumerable<TournamentTeamMemberEntity> members, int? teamSize)
        {
            return new TeamDto
            {
                TeamId = team.Id!.Value,
                TeamName = team.TeamName,
                CaptainUserId = team.CaptainUserId!.Value,
                MemberCount = members.Count(),
                TeamSize = teamSize,
                Members = members.Select(m => new TeamMemberDto
                {
                    UserId = m.UserId!.Value,
                    Username = m.User?.Username ?? "Unknown"
                }).ToList()
            };
        }

        private static TeamDto MapTeamToDto(TournamentTeamEntity team)
        {
            return new TeamDto
            {
                TeamId = team.Id!.Value,
                TeamName = team.TeamName,
                CaptainUserId = team.CaptainUserId!.Value,
                MemberCount = team.Members.Count(),
                Members = team.Members.Select(m => new TeamMemberDto
                {
                    UserId = m.UserId!.Value,
                    Username = m.User?.Username ?? "Unknown"
                }).ToList()
            };
        }
    }
}