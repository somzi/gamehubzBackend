using GameHubz.DataModels.Enums;
using GameHubz.Logic.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Computes the signed-in user's aggregate unread / pending counters (friend requests,
    /// unread DMs, unread match chat, matches awaiting scheduling) and pushes them live to
    /// the user's devices through the <see cref="UserHub"/> whenever an underlying count changes.
    /// </summary>
    public class BadgeService : AppBaseService
    {
        private readonly IHubContext<UserHub> hubContext;
        private readonly IServiceScopeFactory serviceScopeFactory;

        public BadgeService(
            IUnitOfWorkFactory factory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            IHubContext<UserHub> hubContext,
            IServiceScopeFactory serviceScopeFactory)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.hubContext = hubContext;
            this.serviceScopeFactory = serviceScopeFactory;
        }

        public async Task<BadgeCountsDto> GetMyBadgesAsync()
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            return await ComputeAsync(user.UserId);
        }

        public async Task<ApprovalsBreakdownDto> GetMyApprovalsBreakdownAsync()
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            return await GetApprovalsBreakdownAsync(user.UserId);
        }

        /// <summary>
        /// Per-hub and per-tournament breakdown of what the user has to approve, so the client can
        /// cascade the aggregate Hubs-tab dot down to the specific hub card / tournament / Requests
        /// tab. Hub totals = hub join requests + every owned tournament's (registrations + admin-help),
        /// which by construction sums to <see cref="BadgeCountsDto.HubManageTotal"/>.
        /// </summary>
        public async Task<ApprovalsBreakdownDto> GetApprovalsBreakdownAsync(Guid userId)
        {
            var managedHubIds = await this.AppUnitOfWork.UserHubRepository.GetManagedHubIds(userId);
            if (managedHubIds.Count == 0) return new ApprovalsBreakdownDto();

            var regRows = await this.AppUnitOfWork.TournamentRegistrationRepository.GetPendingCountsByTournament(managedHubIds);
            var helpRows = await this.AppUnitOfWork.MatchRepository.GetAdminHelpCountsByTournament(managedHubIds);
            var approvalRows = await this.AppUnitOfWork.MatchRepository.GetPendingApprovalCountsByTournament(managedHubIds);
            var hubJoinRows = await this.AppUnitOfWork.UserHubRequestRepository.GetPendingCountsByHub(managedHubIds);

            // Merge the tournament-scoped sources into one row per tournament.
            var byTournament = new Dictionary<Guid, TournamentApprovalCount>();
            foreach (var r in regRows)
            {
                var row = GetOrAddTournament(byTournament, r.TournamentId, r.HubId, r.Status);
                row.Registrations = r.Count;
            }
            foreach (var h in helpRows)
            {
                var row = GetOrAddTournament(byTournament, h.TournamentId, h.HubId, h.Status);
                row.AdminHelp = h.Count;
            }
            foreach (var a in approvalRows)
            {
                var row = GetOrAddTournament(byTournament, a.TournamentId, a.HubId, a.Status);
                row.ResultApprovals = a.Count;
            }

            // Hub-level join requests (the portion that lives on the Members tab, not in any tournament).
            var hubJoinCounts = managedHubIds.ToDictionary(h => h, _ => 0);
            foreach (var j in hubJoinRows)
                if (hubJoinCounts.ContainsKey(j.HubId)) hubJoinCounts[j.HubId] = j.Count;

            // Hub total = its join requests + the sum of its tournaments' approvals.
            var hubCounts = managedHubIds.ToDictionary(h => h, h => hubJoinCounts[h]);
            foreach (var t in byTournament.Values)
                if (hubCounts.ContainsKey(t.HubId)) hubCounts[t.HubId] += t.Total;

            return new ApprovalsBreakdownDto
            {
                Tournaments = byTournament.Values.Where(t => t.Total > 0).ToList(),
                Hubs = hubCounts
                    .Where(kv => kv.Value > 0)
                    .Select(kv => new HubApprovalCount
                    {
                        HubId = kv.Key,
                        Count = kv.Value,
                        JoinRequests = hubJoinCounts[kv.Key],
                    })
                    .ToList(),
            };
        }

        private static TournamentApprovalCount GetOrAddTournament(
            Dictionary<Guid, TournamentApprovalCount> map, Guid tournamentId, Guid hubId, int status)
        {
            if (!map.TryGetValue(tournamentId, out var row))
            {
                row = new TournamentApprovalCount { TournamentId = tournamentId, HubId = hubId, Status = status };
                map[tournamentId] = row;
            }
            return row;
        }

        public async Task<BadgeCountsDto> ComputeAsync(Guid userId)
        {
            var friendRequests = await this.AppUnitOfWork.FriendRequestRepository.GetIncomingPendingCount(userId);
            var unreadDms = await this.AppUnitOfWork.DirectMessageRepository.GetUnreadCountForUser(userId);

            var activeMatches = await this.AppUnitOfWork.MatchRepository.GetActiveForUserBadge(userId);
            var matchIds = activeMatches.Select(m => m.Id).ToList();
            var unreadByMatch = await this.AppUnitOfWork.MatchChatRepository.GetUnreadCountsByMatch(matchIds, userId);

            // Results an opponent proposed that this user still has to confirm/dispute.
            // Folded into the already-loaded active matches — no extra DB query. A proposal
            // only exists in approval mode, so its mere presence means it's actionable.
            var resultsToConfirm = activeMatches.Count(m =>
                m.ProposedByUserId.HasValue && m.ProposedByUserId.Value != userId);

            // Captain badge: pending join requests on teams this user captains.
            var teamJoinRequests = await this.AppUnitOfWork.TeamJoinRequestRepository.CountPendingForCaptain(userId);

            // Organizer badges: only meaningful for users who manage at least one hub.
            // Resolve the managed-hub set once, then run the three cheap COUNT queries.
            var managedHubIds = await this.AppUnitOfWork.UserHubRepository.GetManagedHubIds(userId);
            int hubJoinRequests = 0, adminHelpRequests = 0, pendingRegistrations = 0, pendingResultApprovals = 0;
            if (managedHubIds.Count > 0)
            {
                hubJoinRequests = await this.AppUnitOfWork.UserHubRequestRepository.CountPendingByHubIds(managedHubIds);
                adminHelpRequests = await this.AppUnitOfWork.MatchRepository.CountAdminHelpForHubs(managedHubIds);
                pendingRegistrations = await this.AppUnitOfWork.TournamentRegistrationRepository.CountPendingForHubs(managedHubIds);
                pendingResultApprovals = await this.AppUnitOfWork.MatchRepository.CountPendingApprovalsForHubs(managedHubIds);
            }

            return new BadgeCountsDto
            {
                FriendRequests = friendRequests,
                UnreadDirectMessages = unreadDms,
                UnreadMatchMessages = unreadByMatch.Values.Sum(),
                MatchesWithUnreadChat = unreadByMatch.Count,
                MatchesToSchedule = activeMatches.Count(m => m.Status == MatchStatus.Pending),
                ResultsToConfirm = resultsToConfirm,
                TeamJoinRequests = teamJoinRequests,
                HubJoinRequests = hubJoinRequests,
                AdminHelpRequests = adminHelpRequests,
                PendingRegistrations = pendingRegistrations,
                PendingResultApprovals = pendingResultApprovals,
            };
        }

        /// <summary>
        /// Recomputes badges for a user and pushes them to their UserHub group. Best-effort:
        /// failures are swallowed so a notification problem never breaks the triggering action.
        /// </summary>
        public async Task PushAsync(Guid userId)
        {
            try
            {
                var dto = await ComputeAsync(userId);
                await this.hubContext.Clients
                    .Group(UserHub.GroupName(userId))
                    .SendAsync("BadgesUpdated", dto);
            }
            catch
            {
                // best-effort — never let a badge push break the underlying mutation
            }
        }

        /// <summary>
        /// Refreshes the organizer badges (pending registrations / admin-help) for everyone who
        /// manages the tournament's hub — the owner plus any hub admins. Best-effort; used when a
        /// registration or admin-help state changes so the managers' counters update live.
        /// Runs in the background on its own DI scope (same F72 pattern as NotificationService):
        /// the recompute costs ~10 queries per manager, which must not extend the request that
        /// triggered it. Every call site saves first, so the fresh context reads committed state.
        /// </summary>
        public Task PushToTournamentManagersAsync(Guid tournamentId)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = this.serviceScopeFactory.CreateScope();
                    var badgeService = scope.ServiceProvider.GetRequiredService<BadgeService>();
                    await badgeService.PushToTournamentManagersCoreAsync(tournamentId);
                }
                catch
                {
                    // best-effort — never let a badge push break the underlying mutation
                }
            });

            return Task.CompletedTask;
        }

        // Must only run on a fresh scope's BadgeService instance (see PushToTournamentManagersAsync) —
        // by the time this executes, the triggering request's DbContext may already be disposed.
        private async Task PushToTournamentManagersCoreAsync(Guid tournamentId)
        {
            var ownership = await this.AppUnitOfWork.TournamentRepository.GetHubOwnership(tournamentId);
            if (ownership == null) return;

            var managerIds = (await this.AppUnitOfWork.UserHubRepository.GetManagerUserIds(ownership.HubId)).ToHashSet();
            managerIds.Add(ownership.OwnerUserId); // owner may lack a UserHub row

            foreach (var id in managerIds)
                await PushAsync(id);
        }
    }
}
