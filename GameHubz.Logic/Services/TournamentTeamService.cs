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
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.NotATeamTournament"]);

            if (tournament.Status != TournamentStatus.RegistrationOpen)
                throw new BusinessRuleException(
                    // Waiting to open is not the same as closed — see the note in TournamentRegistrationService.
                    tournament.Status == TournamentStatus.Draft && tournament.RegistrationOpensAt != null
                        ? this.LocalizationService["BusinessRule.RegistrationNotOpenedYet"]
                        : this.LocalizationService["BusinessRule.TournamentRegistrationNotOpen"]);

            var alreadyInTeam = await this.AppUnitOfWork.TournamentTeamMemberRepository.ExistsInTournament(user.UserId, request.TournamentId);
            if (alreadyInTeam)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.UserAlreadyInATeam"]);

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

            return MapTeamsToDto(team, [member], tournament.TeamSize, tournament.AllowReserves, tournament.MaxReserves);
        }

        public async Task<TeamDto> RenameTeam(Guid teamId, RenameTeamRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamNotFound"]);

            if (team.CaptainUserId != user.UserId)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyCaptainRename"]);

            team.TeamName = request.TeamName;
            await this.AppUnitOfWork.TournamentTeamRepository.UpdateEntity(team, this.UserContextReader);
            await this.SaveAsync();

            await InvalidateCache(team.TournamentId!.Value);

            return MapTeamsToDto(
                team,
                team.Members,
                team.Tournament?.TeamSize,
                team.Tournament?.AllowReserves ?? false,
                team.Tournament?.MaxReserves);
        }

        public async Task DeleteTeam(Guid teamId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamNotFound"]);

            if (team.CaptainUserId != user.UserId)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyCaptainDeleteTeam"]);

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
            if (data == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamNotFound"]);

            if (!data.TeamSize.HasValue)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamSizeNotConfigured"]);

            // Capacity is the lineup plus whatever bench the organizer granted, so a team with a
            // full lineup still has room while it has empty bench slots.
            if (data.CurrentMemberCount >= data.RosterCapacity)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamFull"]);

            if (data.UserAlreadyInTournament)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.UserAlreadyInATeam"]);

            bool joinsAsReserve = data.CurrentStarterCount >= data.TeamSize.Value;

            var member = new TournamentTeamMemberEntity
            {
                Id = Guid.NewGuid(),
                TeamId = data.TeamId,
                UserId = user.UserId,
                JoinedAt = DateTime.UtcNow,
                IsReserve = joinsAsReserve
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
                AllowReserves = data.AllowReserves,
                MaxReserves = data.MaxReserves,
                Members = [.. data.Members, new TeamMemberDto { UserId = user.UserId, Username = user.Username, IsReserve = joinsAsReserve }],
                MemberCount = data.CurrentMemberCount + 1,
                StarterCount = data.CurrentStarterCount + (joinsAsReserve ? 0 : 1),
                ReserveCount = data.CurrentMemberCount - data.CurrentStarterCount + (joinsAsReserve ? 1 : 0)
            };
        }

        public async Task KickMember(Guid teamId, Guid userId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamNotFound"]);

            if (team.CaptainUserId != user.UserId)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyCaptainKick"]);

            if (userId == user.UserId)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.CaptainCannotKickSelf"]);

            var member = team.Members.FirstOrDefault(m => m.UserId == userId);
            if (member == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.UserNotTeamMember"]);

            await this.EnsureLineupSurvivesRemoval(team, member);

            await this.AppUnitOfWork.TournamentTeamMemberRepository.SoftDeleteEntity(member, this.UserContextReader);

            await this.PromoteReserveIfLineupShort(team, member);

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
            if (data == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamNotFound"]);

            if (!data.RequiresApproval)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamIsPublicUseJoin"]);

            if (!data.TeamSize.HasValue)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamSizeNotConfigured"]);

            if (data.CurrentMemberCount >= data.RosterCapacity)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamFull"]);

            if (data.UserAlreadyInTournament)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.UserAlreadyInATeam"]);

            var alreadyRequested = await this.AppUnitOfWork.TeamJoinRequestRepository.HasPendingRequest(teamId, user.UserId);
            if (alreadyRequested)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.PendingRequestForTeam"]);

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
                PushText.FromLiteral(data.TeamName),
                PushText.FromKey("Push.TeamJoinRequest.Body", user.Username),
                new { teamId = data.TeamId.ToString(), tournamentId = data.TournamentId.ToString(), type = "teamJoinRequest" });

            return new TeamDto
            {
                TeamId = data.TeamId,
                TeamName = data.TeamName,
                CaptainUserId = data.CaptainUserId,
                TeamSize = data.TeamSize,
                AllowReserves = data.AllowReserves,
                MaxReserves = data.MaxReserves,
                RequiresApproval = true,
                UserRequestStatus = JoinRequestStatus.Pending,
                Members = data.Members,
                MemberCount = data.CurrentMemberCount,
                StarterCount = data.CurrentStarterCount,
                ReserveCount = data.CurrentMemberCount - data.CurrentStarterCount
            };
        }

        // Fire-and-forget push to a single user by id. Resolves the token in the request scope,
        // then sends in the background so the DbContext is never touched off-thread.
        // "You're in the lineup — 3 upcoming games are yours." The count label sits inside the
        // sentence, so it has to be resolved in the SAME language as the sentence.
        private async Task NotifyLineupInAsync(Guid reserveUserId, TournamentTeamEntity team, TournamentEntity tournament, int upcomingGames)
        {
            var target = await this.AppUnitOfWork.UserRepository.GetById(reserveUserId);
            if (target == null) return;

            string? language = target.Language;

            PushText body;
            if (upcomingGames > 0)
            {
                string gamesNote = this.LocalizationService.Plural(
                    upcomingGames, "Push.UpcomingGames.One", "Push.UpcomingGames.Few", "Push.UpcomingGames.Many", language);

                body = PushText.FromKey("Push.TeamLineupIn.Body", gamesNote);
            }
            else
            {
                body = PushText.FromKey("Push.TeamLineupIn.BodyNoGames");
            }

            // Sent even without a push token: the inbox row is written regardless.
            var recipient = PushRecipient.ForUser(reserveUserId, target.PushToken, language);
            var data = new { teamId = team.Id.ToString(), tournamentId = tournament.Id!.Value.ToString(), type = "teamLineupIn" };

            _ = Task.Run(async () =>
            {
                try { await notificationService.SendLocalizedToOneAsync(recipient, PushText.FromLiteral(team.TeamName), body, data); }
                catch { /* fire-and-forget */ }
            });
        }

        private async Task NotifyUserAsync(Guid userId, PushText title, PushText body, object data)
        {
            var target = await this.AppUnitOfWork.UserRepository.GetById(userId);
            if (target == null) return;

            // Sent even without a push token: the inbox row is written regardless.
            var recipient = PushRecipient.ForUser(userId, target.PushToken, target.Language);
            _ = Task.Run(async () =>
            {
                try { await notificationService.SendLocalizedToOneAsync(recipient, title, body, data); }
                catch { /* fire-and-forget */ }
            });
        }

        // Resolves a shared /team/{id} link: returns the team's tournament + a bit of
        // context so the recipient's app can land on the right tournament and offer a
        // join / request. No auth on the data beyond the controller's [Authorize].
        public async Task<TeamShareSummaryDto> GetTeamShareSummary(Guid teamId)
        {
            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamNotFound"]);

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
            if (team == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamNotFound"]);

            if (team.CaptainUserId != user.UserId)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyCaptainViewRequests"]);

            return await this.AppUnitOfWork.TeamJoinRequestRepository.GetPendingRequestsByTeamId(teamId);
        }

        public async Task<TeamDto> ApproveRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.TeamJoinRequestRepository.GetByIdWithTeam(requestId);
            if (request == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.RequestNotFound"]);

            var team = request.Team!;

            if (team.CaptainUserId != user.UserId)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyCaptainApprove"]);

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(team.TournamentId!.Value);

            if (!tournament.TeamSize.HasValue)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamSizeNotConfigured"]);

            int rosterCapacity = RosterCapacity(tournament);
            if (team.Members.Count >= rosterCapacity)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamFull"]);

            var alreadyInTeam = await this.AppUnitOfWork.TournamentTeamMemberRepository.ExistsInTournament(request.UserId!.Value, team.TournamentId!.Value);
            if (alreadyInTeam)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.UserAlreadyInATeam"]);

            var member = new TournamentTeamMemberEntity
            {
                Id = Guid.NewGuid(),
                TeamId = team.Id,
                UserId = request.UserId,
                JoinedAt = DateTime.UtcNow,
                // Lineup first, bench afterwards — see JoinsAsReserve.
                IsReserve = JoinsAsReserve(tournament, team.Members.Count(m => !m.IsReserve))
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
                PushText.FromLiteral(team.TeamName),
                PushText.FromKey("Push.TeamJoinApproved.Body"),
                new { teamId = team.Id.ToString(), tournamentId = team.TournamentId!.Value.ToString(), type = "teamJoinApproved" });

            return MapTeamsToDto(team, [.. team.Members, member], tournament.TeamSize, tournament.AllowReserves, tournament.MaxReserves);
        }

        public async Task RejectRequest(Guid requestId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var request = await this.AppUnitOfWork.TeamJoinRequestRepository.GetByIdWithTeam(requestId);
            if (request == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.RequestNotFound"]);

            if (request.Team!.CaptainUserId != user.UserId)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyCaptainReject"]);

            request.Status = JoinRequestStatus.Rejected;
            await this.AppUnitOfWork.TeamJoinRequestRepository.UpdateEntity(request, this.UserContextReader);

            await this.SaveAsync();

            // Captain's pending-requests badge drops; tell the player their request was declined.
            await this.badgeService.PushAsync(user.UserId);
            await NotifyUserAsync(
                request.UserId!.Value,
                PushText.FromLiteral(request.Team!.TeamName),
                PushText.FromKey("Push.TeamJoinRejected.Body"),
                new { teamId = request.TeamId!.Value.ToString(), tournamentId = request.Team!.TournamentId!.Value.ToString(), type = "teamJoinRejected" });
        }

        public async Task<List<TeamDto>> GetFinalTeamsByTournament(Guid tournamentId)
        {
            var teams = await this.AppUnitOfWork.TournamentTeamRepository.GetFinalByTournamentId(tournamentId);
            if (teams.Count == 0) return new List<TeamDto>();

            // GetFinalByTournamentId doesn't include the Tournament, and the client needs its roster
            // shape here: without AllowReserves / MaxReserves it can't tell a full roster from a full
            // lineup, so the bench would look like it doesn't exist and joining one would be blocked.
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);

            return teams
                .Select(t => MapTeamsToDto(t, t.Members, tournament.TeamSize, tournament.AllowReserves, tournament.MaxReserves))
                .ToList();
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
            if (team == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamNotFound"]);

            var member = team.Members.FirstOrDefault(m => m.UserId == user.UserId);
            if (member == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.UserNotTeamMember"]);

            await this.EnsureLineupSurvivesRemoval(team, member, leavingSelf: true);

            await this.AppUnitOfWork.TournamentTeamMemberRepository.SoftDeleteEntity(member, this.UserContextReader);

            await this.PromoteReserveIfLineupShort(team, member);

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

        /// <summary>
        /// Trades a starter for a reserve. The reserve always lands on the starter's exact game — the
        /// captain never picks which sub-match they enter — so a lineup change can move players in and
        /// out of the side but can never rearrange who faces whom. That matters because the pairing is
        /// drawn at random when the games are created: letting the captain choose a slot after seeing
        /// the draw would turn a substitution into a way to hand-pick match-ups.
        ///
        /// Games already decided keep the player who actually played them — history is never rewritten,
        /// so the swap only touches fixtures still to come.
        /// </summary>
        public async Task<TeamDto> SwapLineupMember(Guid teamId, SwapLineupMemberRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamNotFound"]);

            if (team.CaptainUserId != user.UserId)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyCaptainLineup"]);

            var tournament = team.Tournament
                ?? await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(team.TournamentId!.Value);

            if (!tournament.AllowReserves)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TournamentHasNoReserves"]);

            if (tournament.Status == TournamentStatus.Completed
                || tournament.Status == TournamentStatus.Cancelled
                || tournament.Status == TournamentStatus.Deleted)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TournamentClosedLineup"]);

            if (request.StarterUserId == request.ReserveUserId)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.PickDifferentReserve"]);

            var starter = team.Members.FirstOrDefault(m => m.UserId == request.StarterUserId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.PlayerNotOnTeam"]);
            var reserve = team.Members.FirstOrDefault(m => m.UserId == request.ReserveUserId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.ReserveNotOnTeam"]);

            if (starter.IsReserve)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OutgoingAlreadyOnBench"]);

            if (!reserve.IsReserve)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.IncomingAlreadyInLineup"]);

            // Every fixture the outgoing player is still due to play, across all rounds already on the
            // schedule. Group stages create the whole season up front, so benching someone has to take
            // them out of each remaining round, not just the next one.
            var matches = await this.AppUnitOfWork.MatchRepository.GetAllByTournamentId(tournament.Id!.Value);
            var tieBreakMatchIds = await this.GetTieBreakMatchIds(tournament.Id!.Value, matches);

            var toRepoint = matches
                .Where(m => m.TeamMatchId.HasValue
                    // A tie-break decider is a separate, explicit nomination (SubmitRepresentative) and
                    // may even be a reserve, so a lineup change must not silently reassign it — the
                    // captain nominates again if their pick is no longer the one they want.
                    && !tieBreakMatchIds.Contains(m.Id!.Value)
                    && !IsDecided(m.Status)
                    && (m.HomeUserId == request.StarterUserId || m.AwayUserId == request.StarterUserId))
                .ToList();

            // Guard against the same person appearing twice in one tie: it happens when the incoming
            // reserve already played a game in that tie before being benched, and their old result
            // keeps them in it. Refuse rather than produce a tie where one player has two games.
            var affectedTeamMatchIds = toRepoint.Select(m => m.TeamMatchId!.Value).ToHashSet();
            bool wouldDuplicate = matches.Any(m => m.TeamMatchId.HasValue
                && affectedTeamMatchIds.Contains(m.TeamMatchId.Value)
                && (m.HomeUserId == request.ReserveUserId || m.AwayUserId == request.ReserveUserId));

            if (wouldDuplicate)
            {
                throw new BusinessRuleException(string.Format(
                    this.LocalizationService["BusinessRule.ReserveAlreadyHasGame"],
                    reserve.User?.Username ?? this.LocalizationService["BusinessRule.ThatPlayerFallback"]));
            }

            starter.IsReserve = true;
            reserve.IsReserve = false;
            await this.AppUnitOfWork.TournamentTeamMemberRepository.UpdateEntity(starter, this.UserContextReader);
            await this.AppUnitOfWork.TournamentTeamMemberRepository.UpdateEntity(reserve, this.UserContextReader);

            foreach (var match in toRepoint)
            {
                if (match.HomeUserId == request.StarterUserId) match.HomeUserId = request.ReserveUserId;
                if (match.AwayUserId == request.StarterUserId) match.AwayUserId = request.ReserveUserId;

                // A pending result proposed by the player leaving the lineup is no longer theirs to
                // make — clear it so the incoming player isn't credited with someone else's report.
                if (match.ProposedByUserId == request.StarterUserId)
                {
                    match.ProposedByUserId = null;
                    match.ProposedHomeScore = null;
                    match.ProposedAwayScore = null;
                }

                await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            }

            await this.SaveAsync();

            await InvalidateCache(tournament.Id!.Value);
            await cacheService.RemoveByPatternAsync($"pdf:bracket:{tournament.Id!.Value}:*");

            // Both sides' "upcoming match" lists changed, so refresh their badges and tell them.
            foreach (var affectedUserId in new[] { request.StarterUserId, request.ReserveUserId })
                await this.badgeService.PushAsync(affectedUserId);

            // The games note is interpolated into the sentence, so it must be resolved in the
            // recipient's language — resolved inside NotifyLineupAsync rather than here.
            await NotifyLineupInAsync(request.ReserveUserId, team, tournament, toRepoint.Count);

            await NotifyUserAsync(
                request.StarterUserId,
                PushText.FromLiteral(team.TeamName),
                PushText.FromKey("Push.TeamLineupOut.Body"),
                new { teamId = team.Id.ToString(), tournamentId = tournament.Id!.Value.ToString(), type = "teamLineupOut" });

            return MapTeamsToDto(team, team.Members, tournament.TeamSize, tournament.AllowReserves, tournament.MaxReserves);
        }

        // Completed or closed as a no-show — either way the game has a recorded outcome and the
        // players in it are history. Mirrors the "played" test used by the participant swap.
        private static bool IsDecided(MatchStatus status)
            => status == MatchStatus.Completed || status == MatchStatus.NoShow;

        /// <summary>
        /// Sub-matches that are tie-break deciders rather than lineup games. Identified the same way
        /// the team-match DTO does it (TeamMatchService.GetTeamMatchDetails): the decider is created
        /// at the parent's MatchOrder + 1000. A bare "MatchOrder >= 1000" test would misfire on a
        /// large bracket, where a normal sub-match order is parentOrder * teamSize.
        /// </summary>
        private async Task<HashSet<Guid>> GetTieBreakMatchIds(Guid tournamentId, List<MatchEntity> matches)
        {
            var teamMatches = await this.AppUnitOfWork.TeamMatchRepository.GetByTournamentId(tournamentId);
            var tieBreakThresholdByTeamMatch = teamMatches
                .Where(tm => tm.Id.HasValue)
                .ToDictionary(tm => tm.Id!.Value, tm => (tm.MatchOrder ?? 0) + 1000);

            return matches
                .Where(m => m.Id.HasValue
                    && m.TeamMatchId.HasValue
                    && tieBreakThresholdByTeamMatch.TryGetValue(m.TeamMatchId.Value, out int threshold)
                    && (m.MatchOrder ?? 0) >= threshold)
                .Select(m => m.Id!.Value)
                .ToHashSet();
        }

        // Total roster slots: the lineup plus whatever bench the organizer granted.
        private static int RosterCapacity(TournamentEntity tournament)
            => (tournament.TeamSize ?? 0)
                + (tournament.AllowReserves ? Math.Max(0, tournament.MaxReserves ?? 0) : 0);

        // New members fill the lineup first and only spill onto the bench once it's complete, so a
        // team is never left unable to field a side while it has players sitting out.
        private static bool JoinsAsReserve(TournamentEntity tournament, int currentStarterCount)
            => tournament.AllowReserves && currentStarterCount >= (tournament.TeamSize ?? 0);

        /// <summary>
        /// A team that is already locked into the bracket must always field a full lineup — an empty
        /// slot means a sub-match with nobody in it, which can never be played and stalls the tie.
        /// So once the team is a confirmed participant (or the tournament is running), a starter can
        /// only leave if a reserve is there to take the slot. Swapping is the lineup's only operation;
        /// dropping out of it is not one.
        ///
        /// Before that point the roster is still being assembled and a part-filled team is normal, so
        /// removals stay free — the team simply can't register until the lineup is complete.
        /// </summary>
        private async Task EnsureLineupSurvivesRemoval(
            TournamentTeamEntity team,
            TournamentTeamMemberEntity member,
            bool leavingSelf = false)
        {
            // A reserve leaving never touches the lineup.
            if (member.IsReserve) return;

            var tournament = team.Tournament
                ?? await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(team.TournamentId!.Value);

            bool lockedIn = team.TournamentParticipantId.HasValue
                || tournament.Status == TournamentStatus.InProgress
                || tournament.Status == TournamentStatus.Completed;

            if (!lockedIn) return;

            bool hasReserveToPromote = team.Members.Any(m => m.IsReserve && m.UserId != member.UserId);
            if (hasReserveToPromote) return;

            int lineupSize = tournament.TeamSize ?? 0;
            throw new BusinessRuleException(string.Format(
                leavingSelf
                    ? this.LocalizationService["BusinessRule.CannotLeaveNoReserve"]
                    : this.LocalizationService["BusinessRule.CannotRemoveNoReserve"],
                MemberCountLabel(lineupSize)));
        }

        private string MemberCountLabel(int count) => this.LocalizationService.Plural(
            count, "BusinessRule.PlayerCountOne", "BusinessRule.PlayerCountFew", "BusinessRule.PlayerCountMany");

        /// <summary>
        /// Keeps the lineup at TeamSize after someone leaves: when a starter goes, the longest-serving
        /// reserve steps up. Without this a team could sit on a short lineup with players on the bench
        /// and still pass the roster-count checks that gate registration.
        /// </summary>
        private async Task PromoteReserveIfLineupShort(TournamentTeamEntity team, TournamentTeamMemberEntity removed)
        {
            if (removed.IsReserve) return;

            var promoted = team.Members
                .Where(m => m.IsReserve && m.UserId != removed.UserId)
                .OrderBy(m => m.JoinedAt ?? DateTime.MaxValue)
                .FirstOrDefault();

            if (promoted == null) return;

            promoted.IsReserve = false;
            await this.AppUnitOfWork.TournamentTeamMemberRepository.UpdateEntity(promoted, this.UserContextReader);
        }

        private async Task InvalidateCache(Guid tournamentId)
        {
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
            await cacheService.RemoveByPatternAsync($"bracket:{tournamentId}:*");
            await cacheService.RemoveByPatternAsync($"bracket:v3:{tournamentId}:*");
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveByPatternAsync($"pdf:bracket:{tournamentId}:*");
            await cacheService.RemoveAsync($"tournament_participants:{tournamentId}");
        }

        private static TeamDto MapTeamsToDto(
            TournamentTeamEntity team,
            IEnumerable<TournamentTeamMemberEntity> members,
            int? teamSize,
            bool allowReserves = false,
            int? maxReserves = null)
        {
            var list = members.ToList();

            return new TeamDto
            {
                TeamId = team.Id!.Value,
                TeamName = team.TeamName,
                CaptainUserId = team.CaptainUserId!.Value,
                MemberCount = list.Count,
                TeamSize = teamSize,
                AllowReserves = allowReserves,
                MaxReserves = maxReserves,
                StarterCount = list.Count(m => !m.IsReserve),
                ReserveCount = list.Count(m => m.IsReserve),
                RequiresApproval = team.RequiresApproval,
                Members = list.Select(m => new TeamMemberDto
                {
                    UserId = m.UserId!.Value,
                    Username = m.User?.Username ?? "Unknown",
                    AvatarUrl = m.User?.AvatarUrl ?? null,
                    IsReserve = m.IsReserve
                }).ToList()
            };
        }
    }
}
