using FluentValidation;
using GameHubz.DataModels.Enums;
using Microsoft.Extensions.Configuration;

namespace GameHubz.Logic.Services
{
    public class TournamentParticipantService : AppBaseServiceGeneric<TournamentParticipantEntity, TournamentParticipantDto, TournamentParticipantPost, TournamentParticipantEdit>
    {
        // Share of their fixtures a league / group entrant may already have played and still be
        // swappable. Overridable via Tournaments:ParticipantSwapMaxPlayedPercent so an organizer
        // community can run stricter or looser than the shipped default.
        private const int DefaultSwapMaxPlayedPercent = 25;

        private const int SwapCandidatePageSize = 20;

        private readonly ICacheService cacheService;
        private readonly TournamentAuthorizationService tournamentAuth;
        private readonly BadgeService badgeService;
        private readonly INotificationService notificationService;
        private readonly IConfiguration configuration;

        public TournamentParticipantService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentParticipantEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            ICacheService cacheService,
            TournamentAuthorizationService tournamentAuth,
            BadgeService badgeService,
            INotificationService notificationService,
            IConfiguration configuration) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.cacheService = cacheService;
            this.tournamentAuth = tournamentAuth;
            this.badgeService = badgeService;
            this.notificationService = notificationService;
            this.configuration = configuration;
        }

        protected override IRepository<TournamentParticipantEntity> GetRepository()
            => this.AppUnitOfWork.TournamentParticipantRepository;

        public async Task<List<TournamentParticipantOverview>> GetByTournament(Guid tournamentId)
        {
            string cacheKey = $"tournament_participants:{tournamentId}";
            var cached = await cacheService.GetAsync<List<TournamentParticipantOverview>>(cacheKey);
            if (cached != null) return cached;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);

            List<TournamentParticipantOverview> result;
            if (tournament.IsTeamTournament)
            {
                var teams = await this.AppUnitOfWork.TournamentTeamRepository.GetByTournamentId(tournamentId);

                result = teams.Select(team => new TournamentParticipantOverview
                {
                    Username = team.TeamName,
                    AvatarUrl = team.CaptainUser?.AvatarUrl,
                    UserId = team.CaptainUserId ?? Guid.Empty,
                    IsTeamTournament = true,
                    TeamId = team.Id,
                    TeamName = team.TeamName,
                    CaptainUserId = team.CaptainUserId,
                    MemberCount = team.Members.Count,
                    TeamSize = tournament.TeamSize,
                    StarterCount = team.Members.Count(m => !m.IsReserve),
                    ReserveCount = team.Members.Count(m => m.IsReserve),
                    // Bench first-class in the list rather than filtered out: the roster card shows
                    // who is on the bench, and the lineup is derived from the flag.
                    Members = team.Members.Select(member => new TournamentParticipantMemberOverview
                    {
                        UserId = member.UserId ?? Guid.Empty,
                        Username = member.User?.Username ?? string.Empty,
                        AvatarUrl = member.User?.AvatarUrl,
                        IsReserve = member.IsReserve
                    }).ToList()
                }).ToList();
            }
            else
            {
                var participants = await this.AppUnitOfWork.TournamentParticipantRepository.GetByTournamentId(tournamentId);
                result = participants ?? new List<TournamentParticipantOverview>();
            }

            await cacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(2));

            return result;
        }

        public async Task RemoveUser(Guid tournamentId, Guid userId)
        {
            // F36: ejecting a participant rewrites the bracket inputs and is reserved for tournament
            // managers. Without this check any authenticated user could remove any player.
            await this.EnsureCanManageTournament(tournamentId);

            // Remove every participant and registration row for this user, not just the first.
            // Duplicate rows used to exist (a registration approved more than once), and the old
            // single-row FirstAsync lookups threw "Sequence contains no elements" as soon as one
            // side was already gone — which made the leftover duplicates impossible to remove from
            // the UI. Deleting the full set is also how those legacy duplicates get cleaned up.
            var participants = await this.AppUnitOfWork.TournamentParticipantRepository.GetAllByTournamentAndUser(tournamentId, userId);
            var registrations = await this.AppUnitOfWork.TournamentRegistrationRepository.GetAllByTournamentAndUser(tournamentId, userId);

            foreach (var participant in participants)
            {
                await this.AppUnitOfWork.TournamentParticipantRepository.HardDeleteEntity(participant);
            }

            foreach (var registration in registrations)
            {
                await this.AppUnitOfWork.TournamentRegistrationRepository.HardDeleteEntity(registration);
            }

            await this.SaveAsync();

            await cacheService.RemoveAsync($"tournament_participants:{tournamentId}");
        }

        public async Task RemoveTeam(Guid tournamentId, Guid teamId)
        {
            // F36: removing a team rewrites the bracket inputs — managers only.
            await this.EnsureCanManageTournament(tournamentId);

            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamNotFound"]);

            foreach (var member in team.Members)
            {
                await this.AppUnitOfWork.TournamentTeamMemberRepository.SoftDeleteEntity(member, this.UserContextReader);
            }

            await this.AppUnitOfWork.TournamentTeamRepository.SoftDeleteEntity(team, this.UserContextReader);

            var participant = await this.AppUnitOfWork.TournamentParticipantRepository.GetByTeamId(teamId);
            if (participant != null)
                await this.AppUnitOfWork.TournamentParticipantRepository.HardDeleteEntity(participant);

            var registration = await this.AppUnitOfWork.TournamentRegistrationRepository.GetByTeamId(teamId);
            if (registration != null)
                await this.AppUnitOfWork.TournamentRegistrationRepository.HardDeleteEntity(registration);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"tournament_participants:{tournamentId}");
        }

        // SaveEntity is the only path that adds a new participant (called from registration approval
        // and team registration flow). Invalidate the participants cache so the next read sees the
        // new participant immediately instead of waiting for the 2-minute TTL.
        protected override async Task BeforeSave(TournamentParticipantEntity entity, TournamentParticipantPost inputDto, bool isNew)
        {
            if (entity.TournamentId.HasValue)
            {
                await cacheService.RemoveAsync($"tournament_participants:{entity.TournamentId.Value}");
            }
        }

        /// <summary>
        /// Whether this participant may still be handed over to someone else, and the numbers behind
        /// the verdict so the organizer sees why. Read-only — safe to call while a picker is open.
        /// </summary>
        public async Task<ParticipantSwapEligibilityDto> GetSwapEligibility(Guid tournamentId, Guid userId)
        {
            await this.EnsureCanManageTournament(tournamentId);

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);
            var participant = await this.ResolveSwappableParticipant(tournament, userId);
            var matches = await this.AppUnitOfWork.MatchRepository.GetAllByTournamentId(tournamentId);

            var eligibility = this.AssessSwap(tournament, participant, matches);

            var user = await this.AppUnitOfWork.UserRepository.GetById(userId);
            eligibility.Username = user?.Username ?? string.Empty;
            eligibility.AvatarUrl = user?.AvatarUrl;

            return eligibility;
        }

        /// <summary>
        /// Hub members who could take the outgoing player's place, filtered by an optional username
        /// search. Current entrants are already excluded, so anything in the list is a valid pick.
        /// </summary>
        public async Task<List<ParticipantSwapCandidateDto>> GetSwapCandidates(Guid tournamentId, string? search, int pageNumber)
        {
            await this.EnsureCanManageTournament(tournamentId);

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);
            if (!tournament.HubId.HasValue)
            {
                return new List<ParticipantSwapCandidateDto>();
            }

            // Everyone already involved — solo entrants and, for completeness, team members — so a
            // swap can never produce the same person twice in one tournament.
            var takenUserIds = await this.AppUnitOfWork.TournamentParticipantRepository.GetAllUserIdsByTournamentId(tournamentId);

            var members = await this.AppUnitOfWork.UserHubRepository.GetSwapCandidatesPaged(
                tournament.HubId.Value,
                Math.Max(0, pageNumber),
                SwapCandidatePageSize,
                search,
                takenUserIds,
                tournament.IsExclusive,
                // Country / region scope, so the picker never offers someone the swap would then
                // refuse — same rule the sign-up path applies (see EnsureIncomingUserCanJoin).
                tournament.Countries,
                tournament.Region);

            return members.Select(m => new ParticipantSwapCandidateDto
            {
                UserId = m.UserId,
                Username = m.Username,
                AvatarUrl = m.AvatarUrl,
                HubRole = m.HubRole,
            }).ToList();
        }

        /// <summary>
        /// Hands one participant's slot to another hub member. The participant row changes owner
        /// rather than being deleted and re-created, so the incoming player inherits the seed, group,
        /// standings and every match already played — the bracket itself is never touched, because
        /// matches reference participant ids, not user ids. Mirrors swap_tournament_participant.sql.
        /// </summary>
        public async Task<ParticipantSwapResultDto> SwapParticipant(Guid tournamentId, ParticipantSwapRequest request)
        {
            await this.EnsureCanManageTournament(tournamentId);

            if (request.OutgoingUserId == Guid.Empty || request.IncomingUserId == Guid.Empty)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.SwapNeedsBothPlayers"]);
            }

            if (request.OutgoingUserId == request.IncomingUserId)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.PickDifferentPlayer"]);
            }

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);
            var participant = await this.ResolveSwappableParticipant(tournament, request.OutgoingUserId);
            var matches = await this.AppUnitOfWork.MatchRepository.GetAllByTournamentId(tournamentId);

            var eligibility = this.AssessSwap(tournament, participant, matches);
            if (!eligibility.CanSwap)
            {
                throw new BusinessRuleException(eligibility.BlockReason ?? this.LocalizationService["BusinessRule.SwapNoLongerPossible"]);
            }

            var incomingUser = await this.AppUnitOfWork.UserRepository.GetById(request.IncomingUserId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.PickedPlayerGone"]);

            // UX_TournamentParticipant_Tournament_User (migration 60) is the DB backstop; checking
            // here turns the race-free case into a readable 400 instead of a unique-violation 500.
            if (await this.AppUnitOfWork.TournamentParticipantRepository.ExistsForUser(tournamentId, request.IncomingUserId))
            {
                throw new BusinessRuleException(string.Format(this.LocalizationService["BusinessRule.UserAlreadyInTournament"], incomingUser.Username));
            }

            await this.EnsureIncomingUserCanJoin(tournament, incomingUser);

            var outgoingUser = await this.AppUnitOfWork.UserRepository.GetById(request.OutgoingUserId);

            // ── the swap itself ────────────────────────────────────────────────────────────────
            participant.UserId = request.IncomingUserId;
            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(participant, this.UserContextReader);

            await this.MoveRegistration(tournamentId, request.OutgoingUserId, request.IncomingUserId);

            // The only per-user ids a match carries. HomeUserId/AwayUserId are set on team
            // sub-matches only (null throughout a solo tournament), but a stale ProposedByUserId or
            // AdminHelpRequestedByUserId would leave the departed player's name on a live match.
            foreach (var match in matches)
            {
                bool touched = false;

                if (match.HomeUserId == request.OutgoingUserId)
                {
                    match.HomeUserId = request.IncomingUserId;
                    touched = true;
                }

                if (match.AwayUserId == request.OutgoingUserId)
                {
                    match.AwayUserId = request.IncomingUserId;
                    touched = true;
                }

                if (match.ProposedByUserId == request.OutgoingUserId)
                {
                    match.ProposedByUserId = request.IncomingUserId;
                    touched = true;
                }

                if (match.AdminHelpRequestedByUserId == request.OutgoingUserId)
                {
                    match.AdminHelpRequestedByUserId = request.IncomingUserId;
                    touched = true;
                }

                if (touched)
                {
                    await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
                }
            }

            // Unreachable through the UI (a finished tournament can't be swapped), but a
            // hand-corrected tournament can carry a winner while still InProgress and a winner id
            // pointing at someone no longer in the tournament is worse than rewriting the row.
            if (tournament.WinnerUserId == request.OutgoingUserId)
            {
                tournament.WinnerUserId = request.IncomingUserId;
                await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            }

            // MatchChat rows keep their original author on purpose: the outgoing player really did
            // write those messages, and rewriting them would forge the match's chat history.
            await this.SaveAsync();

            await this.InvalidateAfterSwap(tournamentId, request.OutgoingUserId, request.IncomingUserId);
            this.NotifyAfterSwap(tournament, outgoingUser, incomingUser, eligibility.PlayedMatches);

            return new ParticipantSwapResultDto
            {
                TournamentId = tournamentId,
                ParticipantId = participant.Id!.Value,
                OutgoingUserId = request.OutgoingUserId,
                OutgoingUsername = outgoingUser?.Username ?? string.Empty,
                IncomingUserId = request.IncomingUserId,
                IncomingUsername = incomingUser.Username,
                IncomingAvatarUrl = incomingUser.AvatarUrl,
                InheritedPlayedMatches = eligibility.PlayedMatches,
            };
        }

        /// <summary>
        /// The single source of truth for the swap rule, shared by the read-only check and the write
        /// path so the UI can never show "allowed" for something the swap then rejects.
        /// </summary>
        private ParticipantSwapEligibilityDto AssessSwap(
            TournamentEntity tournament,
            TournamentParticipantEntity participant,
            List<MatchEntity> matches)
        {
            // Compared as a plain Guid on purpose: Guid? == Guid? treats null == null as a match, so a
            // participant with no id would silently "own" every bye (empty away slot) in the bracket
            // and inflate both counts.
            Guid participantId = participant.Id ?? Guid.Empty;

            // Team sub-matches are excluded: they hang off a parent team fixture and are not this
            // participant's own matches (a no-op for solo tournaments, which are the only ones that
            // reach here — ResolveSwappableParticipant rejects team tournaments).
            var own = matches
                .Where(m => m.TeamMatchId == null
                    && (m.HomeParticipantId == participantId || m.AwayParticipantId == participantId))
                .ToList();

            int total = own.Count;
            int played = own.Count(m => m.Status == MatchStatus.Completed || m.Status == MatchStatus.NoShow);

            // League and group formats tolerate a partly-played schedule; a knockout bracket and
            // Swiss pairings do not, because a played result there has already decided who the
            // player's next opponent is.
            bool allowsPartiallyPlayed = tournament.Format == TournamentFormat.League
                || tournament.Format == TournamentFormat.GroupsThenSingleElimination
                || tournament.Format == TournamentFormat.GroupsThenDoubleElimination
                || tournament.Format == TournamentFormat.GroupStageWithKnockout;

            int maxPercent = this.SwapMaxPlayedPercent();

            var result = new ParticipantSwapEligibilityDto
            {
                TournamentId = tournament.Id!.Value,
                UserId = participant.UserId ?? Guid.Empty,
                Format = tournament.Format,
                PlayedMatches = played,
                TotalMatches = total,
                PlayedPercent = total == 0 ? 0 : (int)Math.Round(played * 100.0 / total),
                MaxPlayedPercent = allowsPartiallyPlayed ? maxPercent : null,
                AllowsPartiallyPlayed = allowsPartiallyPlayed,
            };

            if (tournament.Status == TournamentStatus.Completed
                || tournament.Status == TournamentStatus.Cancelled
                || tournament.Status == TournamentStatus.Deleted)
            {
                result.CanSwap = false;
                result.BlockReason = this.LocalizationService["BusinessRule.SwapTournamentClosed"];
                return result;
            }

            if (played == 0)
            {
                result.CanSwap = true;
                return result;
            }

            if (!allowsPartiallyPlayed)
            {
                result.CanSwap = false;
                result.BlockReason = tournament.Format == TournamentFormat.Swiss
                    ? string.Format(this.LocalizationService["BusinessRule.SwapSwissPlayed"], MatchCountLabel(played))
                    : string.Format(this.LocalizationService["BusinessRule.SwapKnockoutPlayed"], MatchCountLabel(played));
                return result;
            }

            // Compared as integers so the verdict never rides on how PlayedPercent rounded:
            // played/total < maxPercent/100.
            result.CanSwap = total == 0 || played * 100 < maxPercent * total;
            if (!result.CanSwap)
            {
                result.BlockReason = string.Format(
                    this.LocalizationService["BusinessRule.SwapTooManyPlayed"], played, MatchCountLabel(total), result.PlayedPercent, maxPercent);
            }

            return result;
        }

        private string MatchCountLabel(int count) => this.LocalizationService.Plural(
            count, "BusinessRule.MatchCountOne", "BusinessRule.MatchCountFew", "BusinessRule.MatchCountMany");

        private int SwapMaxPlayedPercent()
        {
            int configured = this.configuration.GetValue("Tournaments:ParticipantSwapMaxPlayedPercent", DefaultSwapMaxPlayedPercent);

            // A nonsensical value must not silently open the gate (100 = swap whenever) or wedge it
            // shut in a way the UI can't explain.
            return Math.Clamp(configured, 1, 100);
        }

        /// <summary>
        /// Resolves the one participant row a swap may take over, rejecting the shapes where
        /// inheriting the row is not well defined.
        /// </summary>
        private async Task<TournamentParticipantEntity> ResolveSwappableParticipant(TournamentEntity tournament, Guid userId)
        {
            if (tournament.IsTeamTournament)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.TeamTournamentEntrantIsTeam"]);
            }

            var participants = await this.AppUnitOfWork.TournamentParticipantRepository
                .GetAllByTournamentAndUser(tournament.Id!.Value, userId);

            if (participants.Count == 0)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.PlayerNotParticipant"]);
            }

            if (participants.Count > 1)
            {
                // Legacy duplicate rows (see RemoveUser): which one carries the real history is a
                // guess, so refuse rather than pick. Removing the player clears all of them.
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.DuplicateParticipantEntries"]);
            }

            return participants[0];
        }

        private async Task EnsureIncomingUserCanJoin(TournamentEntity tournament, UserEntity incomingUser)
        {
            if (!tournament.HubId.HasValue)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.NoHubNoRosterChange"]);
            }

            var role = await this.AppUnitOfWork.UserHubRepository.GetRole(incomingUser.Id!.Value, tournament.HubId.Value);
            if (role == null)
            {
                throw new BusinessRuleException(string.Format(this.LocalizationService["BusinessRule.UserNotHubMember"], incomingUser.Username));
            }

            // The tournament's own scope. A manager may hand the spot to any hub member, but not to
            // someone the sign-up path would have turned away: a country-scoped tournament must not
            // end up with a player from outside its country list.
            if (!IsWithinTournamentScope(tournament, incomingUser))
            {
                throw new BusinessRuleException(tournament.Countries != null && tournament.Countries.Count > 0
                    ? string.Format(this.LocalizationService["BusinessRule.EntrantCountryRestricted"], incomingUser.Username, string.Join(", ", tournament.Countries))
                    : string.Format(this.LocalizationService["BusinessRule.EntrantRegionRestricted"], incomingUser.Username));
            }

            // Same access rule the feed uses (UserHubRepository.GetHubIdsWithExclusiveAccess): a
            // plain member can't even see an exclusive tournament, so they can't be placed into one.
            if (tournament.IsExclusive
                && role != HubRole.HubOwner
                && role != HubRole.HubAdmin
                && role != HubRole.HubExclusive)
            {
                throw new BusinessRuleException(string.Format(this.LocalizationService["BusinessRule.UserNeedsExclusiveAccess"], incomingUser.Username));
            }
        }

        /// <summary>
        /// Mirrors TournamentRegistrationService.IsEligibleToJoin (and the tournament-feed visibility
        /// rules): a country-scoped tournament takes only players from its country list, everything
        /// else is region-scoped, and a GLOBAL tournament takes everyone. Kept here as a copy rather
        /// than shared, exactly like the registration and feed paths keep their own.
        /// </summary>
        private static bool IsWithinTournamentScope(TournamentEntity tournament, UserEntity user)
        {
            if (tournament.Countries != null && tournament.Countries.Count > 0)
            {
                return !string.IsNullOrEmpty(user.Country) && tournament.Countries.Contains(user.Country!);
            }

            return tournament.Region == RegionType.GLOBAL || tournament.Region == user.Region;
        }

        /// <summary>
        /// Moves the registration row along with the participant so the prijave list and the
        /// registered-count keep matching the roster.
        /// </summary>
        private async Task MoveRegistration(Guid tournamentId, Guid outgoingUserId, Guid incomingUserId)
        {
            var outgoing = await this.AppUnitOfWork.TournamentRegistrationRepository.GetAllByTournamentAndUser(tournamentId, outgoingUserId);
            var incoming = await this.AppUnitOfWork.TournamentRegistrationRepository.GetAllByTournamentAndUser(tournamentId, incomingUserId);

            if (incoming.Count > 0)
            {
                // The replacement had already applied here (pending, or rejected earlier). Approve
                // that row instead of repointing a second one onto the same user, which would show
                // them twice in the registrations list.
                var keep = incoming[0];
                keep.Status = TournamentRegistrationStatus.Approved;
                await this.AppUnitOfWork.TournamentRegistrationRepository.UpdateEntity(keep, this.UserContextReader);

                foreach (var extra in incoming.Skip(1))
                {
                    await this.AppUnitOfWork.TournamentRegistrationRepository.HardDeleteEntity(extra);
                }

                foreach (var registration in outgoing)
                {
                    await this.AppUnitOfWork.TournamentRegistrationRepository.HardDeleteEntity(registration);
                }

                return;
            }

            // No registration on either side is legitimate (a participant can be materialized
            // outside the registration flow) — the participant row is what the bracket reads.
            foreach (var registration in outgoing)
            {
                registration.UserId = incomingUserId;
                registration.Status = TournamentRegistrationStatus.Approved;
                await this.AppUnitOfWork.TournamentRegistrationRepository.UpdateEntity(registration, this.UserContextReader);
            }
        }

        private async Task InvalidateAfterSwap(Guid tournamentId, Guid outgoingUserId, Guid incomingUserId)
        {
            await cacheService.RemoveAsync($"tournament_participants:{tournamentId}");
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{tournamentId}");
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveAsync($"pdf:bracket:{tournamentId}");

            // Both profiles change: the outgoing player loses this tournament's record, the
            // incoming one inherits it.
            foreach (var userId in new[] { outgoingUserId, incomingUserId })
            {
                await cacheService.RemoveAsync($"player_stats:{userId}");
                await cacheService.RemoveAsync($"player_stats_v2:{userId}");
                await cacheService.RemoveAsync($"user_profile:{userId}");
                await cacheService.RemoveByPatternAsync($"user_matches:{userId}:*");
                await cacheService.RemoveByPatternAsync($"user_profile_tournaments:{userId}:*");
                await cacheService.RemoveByPatternAsync($"user_feed:{userId}:*");

                // Upcoming matches / results-to-confirm moved between the two accounts.
                await this.badgeService.PushAsync(userId);
            }
        }

        private void NotifyAfterSwap(
            TournamentEntity tournament,
            UserEntity? outgoingUser,
            UserEntity incomingUser,
            int inheritedPlayedMatches)
        {
            string tournamentId = tournament.Id!.Value.ToString();

            // The count label is part of the sentence, so it has to be resolved in the same
            // language as the sentence — hence the key rather than a pre-built string.
            PushText inheritedNote = inheritedPlayedMatches > 0
                ? PushText.FromKey("Push.SwappedIn.Body", PlayedMatchesKey(inheritedPlayedMatches, incomingUser?.Language))
                : PushText.FromKey("Push.SwappedIn.BodyFirstMatch");

            this.SendPush(
                incomingUser,
                PushText.FromLiteral(tournament.Name),
                inheritedNote,
                new { tournamentId, type = "participantSwappedIn" });

            this.SendPush(
                outgoingUser,
                PushText.FromLiteral(tournament.Name),
                PushText.FromKey("Push.SwappedOut.Body"),
                new { tournamentId, type = "participantSwappedOut" });
        }

        // The push token is already in hand (both users were loaded during the swap), so only the
        // send is fired and forgotten — a failed notification must never break a committed swap.
        private void SendPush(UserEntity? target, PushText title, PushText body, object data)
        {
            if (target?.Id == null) return;

            // Sent even without a push token: the inbox row is written regardless.
            var recipient = PushRecipient.ForUser(target.Id.Value, target.PushToken, target.Language);
            _ = Task.Run(async () =>
            {
                try { await this.notificationService.SendLocalizedToOneAsync(recipient, title, body, data); }
                catch { /* fire-and-forget */ }
            });
        }

        // "3 matches" / "1 match" in the recipient's language, for embedding in Push.SwappedIn.Body.
        private string PlayedMatchesKey(int count, string? language)
            => this.LocalizationService.Plural(
                count, "Push.PlayedMatches.One", "Push.PlayedMatches.Few", "Push.PlayedMatches.Many", language);

        private async Task EnsureCanManageTournament(Guid tournamentId)
        {
            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId))
            {
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);
            }
        }
    }
}