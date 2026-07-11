using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class TournamentTeamService : AppBaseService
    {
        private readonly ICacheService cacheService;
        private readonly INotificationService notificationService;
        private readonly BadgeService badgeService;

        public TournamentTeamService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            ICacheService cacheService,
            INotificationService notificationService,
            BadgeService badgeService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.cacheService = cacheService;
            this.notificationService = notificationService;
            this.badgeService = badgeService;
        }

        public async Task<TeamDto> CreateTeam(CreateTeamRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(request.TournamentId);

            if (!tournament.IsTeamTournament)
                throw new BusinessRuleException("This tournament is not a team tournament.");

            if (tournament.Status != TournamentStatus.RegistrationOpen)
                throw new BusinessRuleException("Tournament registration is not open.");

            var alreadyInTeam = await this.AppUnitOfWork.TournamentTeamMemberRepository.ExistsInTournament(user.UserId, request.TournamentId);
            if (alreadyInTeam)
                throw new BusinessRuleException("User is already in a team for this tournament.");

            var team = new TournamentTeamEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = request.TournamentId,
                TeamName = request.TeamName,
                CaptainUserId = user.UserId,
                RequiresApproval = request.RequiresApproval,
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
            if (team == null) throw new BusinessRuleException("Team not found.");

            if (team.CaptainUserId != user.UserId)
                throw new BusinessRuleException("Only the captain can rename the team.");

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
            if (team == null) throw new BusinessRuleException("Team not found.");

            if (team.CaptainUserId != user.UserId)
                throw new BusinessRuleException("Only the captain can delete the team.");

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

            var data = await this.AppUnitOfWork.TournamentTeamRepository.GetTeamForJoin(teamId, user.UserId);
            if (data == null) throw new BusinessRuleException("Team not found.");

            if (!data.TeamSize.HasValue)
                throw new BusinessRuleException("Tournament team size is not configured.");

            if (data.CurrentMemberCount >= data.TeamSize.Value)
                throw new BusinessRuleException("Team is already full.");

            if (data.UserAlreadyInTournament)
                throw new BusinessRuleException("User is already in a team for this tournament.");

            var member = new TournamentTeamMemberEntity
            {
                Id = Guid.NewGuid(),
                TeamId = data.TeamId,
                UserId = user.UserId,
                JoinedAt = DateTime.UtcNow
            };

            await this.AppUnitOfWork.TournamentTeamMemberRepository.AddEntity(member, this.UserContextReader);
            await this.SaveAsync();

            await InvalidateCache(data.TournamentId);

            return new TeamDto
            {
                TeamId = data.TeamId,
                TeamName = data.TeamName,
                CaptainUserId = data.CaptainUserId,
                TeamSize = data.TeamSize,
                Members = [.. data.Members, new TeamMemberDto { UserId = user.UserId, Username = user.Username }],
                MemberCount = data.CurrentMemberCount + 1
            };
        }

        public async Task KickMember(Guid teamId, Guid userId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new BusinessRuleException("Team not found.");

            if (team.CaptainUserId != user.UserId)
                throw new BusinessRuleException("Only the captain can kick members.");

            if (userId == user.UserId)
                throw new BusinessRuleException("Captain cannot kick themselves.");

            var member = team.Members.FirstOrDefault(m => m.UserId == userId);
            if (member == null) throw new BusinessRuleException("User is not a member of this team.");

            await this.AppUnitOfWork.TournamentTeamMemberRepository.SoftDeleteEntity(member, this.UserContextReader);

            var joinRequest = await this.AppUnitOfWork.TeamJoinRequestRepository.GetApprovedByTeamAndUser(teamId, userId);
            if (joinRequest != null)
                await this.AppUnitOfWork.TeamJoinRequestRepository.HardDeleteEntity(joinRequest);

            await this.SaveAsync();

            await InvalidateCache(team.TournamentId!.Value);
        }

        public async Task<TeamDto> RequestJoin(Guid teamId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var data = await this.AppUnitOfWork.TournamentTeamRepository.GetTeamForJoin(teamId, user.UserId);
            if (data == null) throw new BusinessRuleException("Team not found.");

            if (!data.RequiresApproval)
                throw new BusinessRuleException("This team is public. Use the join endpoint instead.");

            if (!data.TeamSize.HasValue)
                throw new BusinessRuleException("Tournament team size is not configured.");

            if (data.CurrentMemberCount >= data.TeamSize.Value)
                throw new BusinessRuleException("Team is already full.");

            if (data.UserAlreadyInTournament)
                throw new BusinessRuleException("User is already in a team for this tournament.");

            var alreadyRequested = await this.AppUnitOfWork.TeamJoinRequestRepository.HasPendingRequest(teamId, user.UserId);
            if (alreadyRequested)
                throw new BusinessRuleException("You already have a pending request for this team.");

            var request = new TeamJoinRequestEntity
            {
                Id = Guid.NewGuid(),
                TeamId = data.TeamId,
                UserId = user.UserId,
                Status = JoinRequestStatus.Pending,
                CreatedOn = DateTime.UtcNow
            };

            await this.AppUnitOfWork.TeamJoinRequestRepository.AddEntity(request, this.UserContextReader);
            await this.SaveAsync();

            // The captain has a new join request waiting — bump their badge and push.
            await this.badgeService.PushAsync(data.CaptainUserId);
            await NotifyUserAsync(
                data.CaptainUserId,
                data.TeamName,
                $"{user.Username} asked to join your team.",
                new { teamId = data.TeamId.ToString(), tournamentId = data.TournamentId.ToString(), type = "teamJoinRequest" });

            return new TeamDto
            {
                TeamId = data.TeamId,
                TeamName = data.TeamName,
                CaptainUserId = data.CaptainUserId,
                TeamSize = data.TeamSize,
                RequiresApproval = true,
                UserRequestStatus = JoinRequestStatus.Pending,
                Members = data.Members,
                MemberCount = data.CurrentMemberCount
            };
        }

        // Fire-and-forget push to a single user by id. Resolves the token in the request scope,
        // then sends in the background so the DbContext is never touched off-thread.
        private async Task NotifyUserAsync(Guid userId, string title, string body, object data)
        {
            var target = await this.AppUnitOfWork.UserRepository.GetById(userId);
            if (string.IsNullOrEmpty(target?.PushToken)) return;

            var token = target.PushToken!;
            _ = Task.Run(async () =>
            {
                try { await notificationService.SendToOneAsync(token, title, body, data); }
                catch { /* fire-and-forget */ }
            });
        }

        // Resolves a shared /team/{id} link: returns the team's tournament + a bit of
        // context so the recipient's app can land on the right tournament and offer a
        // join / request. No auth on the data beyond the controller's [Authorize].
        public async Task<TeamShareSummaryDto> GetTeamShareSummary(Guid teamId)
        {
            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new BusinessRuleException("Team not found.");

            return new TeamShareSummaryDto
            {
                TeamId = team.Id!.Value,
                TournamentId = team.TournamentId!.Value,
                TeamName = team.TeamName,
                RequiresApproval = team.RequiresApproval,
                MemberCount = team.Members.Count,
                TeamSize = team.Tournament?.TeamSize,
            };
        }

        public async Task<List<TeamDto>> GetTeamsByTournament(Guid tournamentId)
        {
            return await this.AppUnitOfWork.TournamentTeamRepository.GetTeamsDtoByTournamentId(tournamentId);
        }

        public async Task<List<TeamDto>> GetTeamsByTournamentForUser(Guid tournamentId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            return await this.AppUnitOfWork.TournamentTeamRepository.GetTeamsDtoByTournamentId(tournamentId, user.UserId);
        }

        public async Task<List<TeamJoinRequestDto>> GetPendingRequests(Guid teamId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetById(teamId);
            if (team == null) throw new BusinessRuleException("Team not found.");

            if (team.CaptainUserId != user.UserId)
                throw new BusinessRuleException("Only the captain can view join requests.");

            return await this.AppUnitOfWork.TeamJoinRequestRepository.GetPendingRequestsByTeamId(teamId);
        }

        public async Task<TeamDto> ApproveRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.TeamJoinRequestRepository.GetByIdWithTeam(requestId);
            if (request == null) throw new BusinessRuleException("Request not found.");

            var team = request.Team!;

            if (team.CaptainUserId != user.UserId)
                throw new BusinessRuleException("Only the captain can approve requests.");

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(team.TournamentId!.Value);

            if (!tournament.TeamSize.HasValue)
                throw new BusinessRuleException("Tournament team size is not configured.");

            if (team.Members.Count >= tournament.TeamSize.Value)
                throw new BusinessRuleException("Team is already full.");

            var alreadyInTeam = await this.AppUnitOfWork.TournamentTeamMemberRepository.ExistsInTournament(request.UserId!.Value, team.TournamentId!.Value);
            if (alreadyInTeam)
                throw new BusinessRuleException("User is already in a team for this tournament.");

            var member = new TournamentTeamMemberEntity
            {
                Id = Guid.NewGuid(),
                TeamId = team.Id,
                UserId = request.UserId,
                JoinedAt = DateTime.UtcNow
            };

            await this.AppUnitOfWork.TournamentTeamMemberRepository.AddEntity(member, this.UserContextReader);

            request.Status = JoinRequestStatus.Approved;
            await this.AppUnitOfWork.TeamJoinRequestRepository.UpdateEntity(request, this.UserContextReader);

            await this.SaveAsync();

            await InvalidateCache(team.TournamentId!.Value);

            // Captain's pending-requests badge drops; tell the approved player.
            await this.badgeService.PushAsync(user.UserId);
            await NotifyUserAsync(
                request.UserId!.Value,
                team.TeamName,
                "You've been added to the team.",
                new { teamId = team.Id.ToString(), tournamentId = team.TournamentId!.Value.ToString(), type = "teamJoinApproved" });

            return MapTeamsToDto(team, [.. team.Members, member], tournament.TeamSize);
        }

        public async Task RejectRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.TeamJoinRequestRepository.GetByIdWithTeam(requestId);
            if (request == null) throw new BusinessRuleException("Request not found.");

            if (request.Team!.CaptainUserId != user.UserId)
                throw new BusinessRuleException("Only the captain can reject requests.");

            request.Status = JoinRequestStatus.Rejected;
            await this.AppUnitOfWork.TeamJoinRequestRepository.UpdateEntity(request, this.UserContextReader);

            await this.SaveAsync();

            // Captain's pending-requests badge drops; tell the player their request was declined.
            await this.badgeService.PushAsync(user.UserId);
            await NotifyUserAsync(
                request.UserId!.Value,
                request.Team!.TeamName,
                "Your request to join the team was declined.",
                new { teamId = request.TeamId!.Value.ToString(), tournamentId = request.Team!.TournamentId!.Value.ToString(), type = "teamJoinRejected" });
        }

        public async Task<List<TeamDto>> GetFinalTeamsByTournament(Guid tournamentId)
        {
            var teams = await this.AppUnitOfWork.TournamentTeamRepository.GetFinalByTournamentId(tournamentId);

            return teams.Select(t => MapTeamsToDto(t, t.Members, 0)).ToList();
        }

        public async Task<TeamDto> GetTeamByTournament(Guid tournamentId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            return await this.AppUnitOfWork.TournamentTeamRepository.GetTeamDtoByTournamentId(tournamentId, user.UserId);
        }

        public async Task LeaveTeam(Guid teamId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new BusinessRuleException("Team not found.");

            var member = team.Members.FirstOrDefault(m => m.UserId == user.UserId);
            if (member == null) throw new BusinessRuleException("User is not a member of this team.");

            await this.AppUnitOfWork.TournamentTeamMemberRepository.SoftDeleteEntity(member, this.UserContextReader);

            var joinRequest = await this.AppUnitOfWork.TeamJoinRequestRepository.GetApprovedByTeamAndUser(teamId, user.UserId);
            if (joinRequest != null)
                await this.AppUnitOfWork.TeamJoinRequestRepository.HardDeleteEntity(joinRequest);

            var remainingMembers = team.Members.Where(m => m.UserId != user.UserId).ToList();

            if (remainingMembers.Count == 0)
            {
                await this.AppUnitOfWork.TournamentTeamRepository.SoftDeleteEntity(team, this.UserContextReader);
            }
            else if (team.CaptainUserId == user.UserId)
            {
                team.CaptainUserId = remainingMembers.First().UserId;
                await this.AppUnitOfWork.TournamentTeamRepository.UpdateEntity(team, this.UserContextReader);
            }

            await this.SaveAsync();

            await InvalidateCache(team.TournamentId!.Value);
        }

        private async Task InvalidateCache(Guid tournamentId)
        {
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{tournamentId}");
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveAsync($"tournament_participants:{tournamentId}");
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
                RequiresApproval = team.RequiresApproval,
                Members = members.Select(m => new TeamMemberDto
                {
                    UserId = m.UserId!.Value,
                    Username = m.User?.Username ?? "Unknown",
                    AvatarUrl = m.User?.AvatarUrl ?? null
                }).ToList()
            };
        }
    }
}