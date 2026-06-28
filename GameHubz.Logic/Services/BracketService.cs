using GameHubz.Common.Consts;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class BracketService : AppBaseService
    {
        private readonly HubActivityService hubActivityService;
        private readonly ICacheService cacheService;
        private readonly INotificationService notificationService;
        private readonly TournamentAuthorizationService tournamentAuth;
        private readonly BadgeService badgeService;

        public BracketService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            HubActivityService hubActivityService,
            ICacheService cacheService,
            INotificationService notificationService,
            TournamentAuthorizationService tournamentAuth,
            BadgeService badgeService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.hubActivityService = hubActivityService;
            this.cacheService = cacheService;
            this.notificationService = notificationService;
            this.tournamentAuth = tournamentAuth;
            this.badgeService = badgeService;
        }

        public async Task<TournamentStructureDto> GetTournamentStructure(Guid tournamentId)
        {
            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContext();
            Guid currentUserId = currentUser?.UserId ?? Guid.Empty;
            bool isAdmin = currentUser?.RoleEnum == UserRoleEnum.Admin;

            string cacheKey = $"bracket:{tournamentId}";
            var cachedBracket = await cacheService.GetAsync<TournamentStructureDto>(cacheKey);

            var tournament = cachedBracket == null
                            ? await this.AppUnitOfWork.TournamentRepository.GetWithFullDetails(tournamentId)
                            : null;

            if (cachedBracket == null && tournament == null)
                throw new Exception("Tournament not found");

            Guid hubOwnerId = tournament?.Hub?.UserId ?? Guid.Empty;
            bool isPrivileged = isAdmin || currentUserId == hubOwnerId;

            if (cachedBracket != null)
            {
                // Patch CanRevert on the cached snapshot for the current user
                PatchCanRevert(cachedBracket, currentUserId, isPrivileged);
                return cachedBracket;
            }

            var response = await BuildStructureResponse(tournament!, currentUserId, isPrivileged, cacheKey);

            // Patch CanRevert after caching so the cached copy stays user-agnostic
            PatchCanRevert(response, currentUserId, isPrivileged);

            return response;
        }

        /// <summary>
        /// v2 of the structure endpoint. Identical payload to v1 plus <see cref="TournamentStructureDto.CanManage"/>,
        /// and it recognises hub admins (not just the owner / platform admin) as privileged so they receive the
        /// same CanRevert flags. v1 is left untouched so the legacy client in review keeps its exact behaviour.
        /// </summary>
        public async Task<TournamentStructureDto> GetTournamentStructureV2(Guid tournamentId)
        {
            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContext();
            Guid currentUserId = currentUser?.UserId ?? Guid.Empty;

            string cacheKey = $"bracket:{tournamentId}";
            var cachedBracket = await cacheService.GetAsync<TournamentStructureDto>(cacheKey);

            var tournament = cachedBracket == null
                            ? await this.AppUnitOfWork.TournamentRepository.GetWithFullDetails(tournamentId)
                            : null;

            if (cachedBracket == null && tournament == null)
                throw new Exception("Tournament not found");

            bool canManage = currentUser != null
                && await this.tournamentAuth.CanManageTournamentAsync(tournamentId, currentUser);

            if (cachedBracket != null)
            {
                PatchCanRevert(cachedBracket, currentUserId, canManage);
                cachedBracket.CanManage = canManage;
                return cachedBracket;
            }

            var response = await BuildStructureResponse(tournament!, currentUserId, canManage, cacheKey);

            PatchCanRevert(response, currentUserId, canManage);
            response.CanManage = canManage;

            return response;
        }

        /// <summary>
        /// v3 of the structure endpoint. Identical to v2 except group-stage matches in team
        /// tournaments are returned as one Team-vs-Team card per <see cref="TeamMatchEntity"/>
        /// (instead of the per-player sub-match cards v1/v2 emit). v1/v2 are left byte-identical so
        /// the live app keeps working until clients update. Cached under a separate key so the two
        /// group shapes never cross-pollute the shared bracket cache.
        /// </summary>
        public async Task<TournamentStructureDto> GetTournamentStructureV3(Guid tournamentId)
        {
            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContext();
            Guid currentUserId = currentUser?.UserId ?? Guid.Empty;

            string cacheKey = $"bracket:v3:{tournamentId}";
            var cachedBracket = await cacheService.GetAsync<TournamentStructureDto>(cacheKey);

            var tournament = cachedBracket == null
                            ? await this.AppUnitOfWork.TournamentRepository.GetWithFullDetails(tournamentId)
                            : null;

            if (cachedBracket == null && tournament == null)
                throw new Exception("Tournament not found");

            bool canManage = currentUser != null
                && await this.tournamentAuth.CanManageTournamentAsync(tournamentId, currentUser);

            if (cachedBracket != null)
            {
                PatchCanRevert(cachedBracket, currentUserId, canManage);
                cachedBracket.CanManage = canManage;
                return cachedBracket;
            }

            var response = await BuildStructureResponse(tournament!, currentUserId, canManage, cacheKey, teamGroupMatches: true);

            PatchCanRevert(response, currentUserId, canManage);
            response.CanManage = canManage;

            return response;
        }

        private async Task<TournamentStructureDto> BuildStructureResponse(
            TournamentEntity tournament, Guid currentUserId, bool isPrivileged, string cacheKey, bool teamGroupMatches = false)
        {
            var response = new TournamentStructureDto
            {
                TournamentId = tournament!.Id!.Value,
                Name = tournament.Name,
                Format = tournament.Format,
                Status = tournament.Status,
                IsTeamTournament = tournament.IsTeamTournament,
                Stages = new List<TournamentStageStructureDto>(),
                HubOwnerId = tournament.Hub!.UserId,
                QualifiersPerGroup = tournament.QualifiersPerGroup,
                RequireResultApproval = tournament.RequireResultApproval
            };

            foreach (var stageEntity in (tournament.TournamentStages ?? []).OrderBy(s => s.Order))
            {
                var stageDto = new TournamentStageStructureDto
                {
                    StageId = stageEntity.Id!.Value,
                    Type = stageEntity.Type,
                    Order = stageEntity.Order,
                    Name = stageEntity.Name ?? stageEntity.Type.ToString()
                };

                if (stageEntity.Type == StageType.SingleEliminationBracket
                    || stageEntity.Type == StageType.DoubleEliminationWinnersBracket
                    || stageEntity.Type == StageType.DoubleEliminationLosersBracket
                    || stageEntity.Type == StageType.PlayIn)
                {
                    // LB matches that the DE generator collapsed (no participants, no winner —
                    // both upstream feeders were byes) get filtered out of the structure so the
                    // UI doesn't render empty "TBD vs TBD" placeholders for bypassed rounds.
                    bool dropCollapsedByes = stageEntity.Type == StageType.DoubleEliminationLosersBracket;
                    stageDto.Rounds = tournament.IsTeamTournament
                        ? MapTeamBracketRounds(stageEntity.TeamMatches, dropCollapsedByes)
                        : MapBracketRounds(stageEntity.Matches, currentUserId, isPrivileged, dropCollapsedByes);
                }
                else if (stageEntity.Type == StageType.GroupStage || stageEntity.Type == StageType.League
                    || stageEntity.Type == StageType.Swiss)
                {
                    // Swiss renders like a league: one group with standings + matches grouped by round.
                    stageDto.Groups = await MapGroups(stageEntity, currentUserId, isPrivileged, teamGroupMatches);
                }

                response.Stages.Add(stageDto);
            }

            await cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(5));

            return response;
        }

        #region 1. Tournament Generation Entry Points

        public async Task CreateBracket(CreateBracketRequest request)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(request.TournamentId);

            if (tournament == null)
                throw new Exception("Tournament not found");

            if (tournament.TournamentParticipants == null || tournament.TournamentParticipants.Count < 2)
                throw new Exception("Not enough participants to start tournament.");

            // Every participating team must be full: sub-matches are created per roster slot,
            // so an under-filled team produces matches with no player that can never be played,
            // which permanently stalls the bracket. The frontend gates this, but enforce it here too.
            if (tournament.IsTeamTournament && tournament.TeamSize.HasValue)
            {
                int requiredSize = tournament.TeamSize.Value;
                var teams = await this.AppUnitOfWork.TournamentTeamRepository.GetFinalByTournamentId(request.TournamentId);

                var incompleteTeams = teams
                    .Select(t => new { t.TeamName, MemberCount = t.Members.Count(m => m.UserId.HasValue) })
                    .Where(t => t.MemberCount < requiredSize)
                    .Select(t => $"{t.TeamName} ({t.MemberCount}/{requiredSize})")
                    .ToList();

                if (incompleteTeams.Count > 0)
                    throw new Exception($"All teams must be full before starting. Incomplete: {string.Join(", ", incompleteTeams)}");
            }

            var tournamentId = request.TournamentId;

            TimeSpan? roundDuration = tournament.RoundDurationMinutes.HasValue
                ? TimeSpan.FromMinutes(tournament.RoundDurationMinutes.Value)
                : null;

            // Atomic claim (CAS on Status): exactly one request may generate the bracket.
            // Without this, a double-tap or client retry produced two complete brackets —
            // the InProgress flip used to happen only at the end of generation. Generators
            // no longer touch Status themselves.
            var previousStatus = tournament.Status;
            bool claimed = await this.AppUnitOfWork.TournamentRepository.TryClaimBracketGeneration(tournamentId);
            if (!claimed)
                throw new Exception("Tournament already started.");

            try
            {
                await GenerateBracketForFormat(tournament, tournamentId, roundDuration);
            }
            catch
            {
                // Hand the claim back so a failed generation (e.g. invalid Swiss/group config
                // detected inside a generator) can be corrected and retried.
                await this.AppUnitOfWork.TournamentRepository.RestoreBracketGenerationClaim(tournamentId, previousStatus);
                throw;
            }

            await cacheService.RemoveAsync($"tournament:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{tournamentId}");
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveAsync($"pdf:bracket:{tournamentId}");
            await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentLive);

            // Notify all participants that the tournament is now live
            await SendNotification(tournament, tournamentId);
        }

        /// <summary>
        /// Tears the knockout bracket back down (admin only) so a group-stage result can be corrected
        /// and the bracket re-drawn. The group stage itself is left untouched — only the knockout stage
        /// (and, for double-elimination, its losers bracket) is cleared. Once empty, group results
        /// become editable again and the bracket re-draws either automatically (when the corrected
        /// group finishes) or on demand via <see cref="DrawKnockoutFromGroups"/>.
        /// </summary>
        public async Task ResetKnockoutStage(Guid tournamentId)
        {
            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId, currentUser))
                throw new BusinessRuleException("Only the hub owner or an admin can reset the bracket.");

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);

            // Only a group stage feeding a knockout can be reset this way (order 1 = groups, 2 = knockout).
            var groupStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 1);
            if (groupStage == null || groupStage.Type != StageType.GroupStage)
                throw new BusinessRuleException("This tournament has no group stage, so there is no bracket to reset.");

            var knockoutStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 2);
            if (knockoutStage == null)
                throw new BusinessRuleException("No knockout stage was found for this tournament.");

            int removed = await TearDownKnockoutMatchesAsync(tournament, knockoutStage);
            if (removed == 0)
                throw new BusinessRuleException("The bracket has not been drawn yet, so there is nothing to reset.");

            // The knockout may have already crowned a champion — roll the tournament back to in-progress.
            if (tournament.Status == TournamentStatus.Completed)
            {
                tournament.Status = TournamentStatus.InProgress;
                tournament.WinnerUserId = null;
                tournament.WinnerTeamId = null;
                await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            }

            await this.SaveAsync();
            await InvalidateTournamentBracketCaches(tournamentId);
        }

        /// <summary>
        /// Draws (or re-draws) the knockout bracket from the finished group standings on demand (admin
        /// only). Used after <see cref="ResetKnockoutStage"/> to re-seed without changing any result, or
        /// when the organiser wants to draw the bracket manually. No-op-safe: the underlying check
        /// refuses to draw twice or before the groups are complete.
        /// </summary>
        public async Task DrawKnockoutFromGroups(Guid tournamentId)
        {
            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId, currentUser))
                throw new BusinessRuleException("Only the hub owner or an admin can draw the bracket.");

            var groupStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 1);
            if (groupStage == null || groupStage.Type != StageType.GroupStage)
                throw new BusinessRuleException("This tournament has no group stage to draw a bracket from.");

            var knockoutStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 2);
            if (knockoutStage != null && await this.AppUnitOfWork.MatchRepository.HasMatchesForStage(knockoutStage.Id!.Value))
                throw new BusinessRuleException("The bracket is already drawn. Reset it first if you want to re-draw.");

            // Serialised like the automatic draw: CheckAndAdvanceGroupStage is a check-then-act over
            // many rows and must not race a concurrent result finalise.
            await this.AppUnitOfWork.TournamentRepository.AcquireAdvancementLock(tournamentId);
            try
            {
                await CheckAndAdvanceGroupStage(tournamentId, groupStage.Id!.Value);
            }
            finally
            {
                await this.AppUnitOfWork.TournamentRepository.ReleaseAdvancementLock(tournamentId);
            }

            await InvalidateTournamentBracketCaches(tournamentId);
        }

        /// <summary>
        /// Manual re-seed (admin only): exchange the bracket positions of two first-round participants.
        /// When both sit in unplayed real fixtures the swap is surgical — only those two change (team
        /// sub-matches rebuilt for them alone). When a bye is involved it falls back to a full re-seed
        /// from the swapped slots. Either way nothing may have been played yet (reset to re-seed after play).
        /// </summary>
        public async Task SwapBracketParticipants(Guid tournamentId, Guid participantAId, Guid participantBId)
        {
            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId, currentUser))
                throw new BusinessRuleException("Only the hub owner or an admin can edit the bracket seeding.");

            if (participantAId == participantBId)
                throw new BusinessRuleException("Pick two different teams to swap.");

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);

            var knockoutStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 2);
            if (knockoutStage == null
                || (knockoutStage.Type != StageType.SingleEliminationBracket && knockoutStage.Type != StageType.DoubleEliminationWinnersBracket))
                throw new BusinessRuleException("This tournament has no knockout bracket to edit.");

            bool useDoubleKnockout = knockoutStage.Type == StageType.DoubleEliminationWinnersBracket;

            if (tournament.IsTeamTournament)
            {
                var allMatches = await this.AppUnitOfWork.TeamMatchRepository.GetByStageId(knockoutStage.Id!.Value);
                var firstRound = allMatches.Where(tm => (tm.RoundNumber ?? 1) == 1).ToList();

                var (matchA, aIsHome) = LocateTeamSlot(firstRound, participantAId);
                var (matchB, bIsHome) = LocateTeamSlot(firstRound, participantBId);

                if (matchA != null && matchB != null)
                {
                    // Surgical: both teams sit in unplayed real fixtures — swap in place, rebuild only the
                    // affected sub-matches (Distinct collapses a home-vs-away swap inside one match).
                    if (aIsHome) matchA.HomeTeamParticipantId = participantBId; else matchA.AwayTeamParticipantId = participantBId;
                    if (bIsHome) matchB.HomeTeamParticipantId = participantAId; else matchB.AwayTeamParticipantId = participantAId;

                    var affected = new[] { matchA, matchB }.Distinct().ToList();
                    var partIds = affected
                        .SelectMany(tm => new[] { tm.HomeTeamParticipantId, tm.AwayTeamParticipantId })
                        .Where(idv => idv.HasValue).Select(idv => idv!.Value).Distinct().ToList();
                    var participants = new List<TournamentParticipantEntity>();
                    foreach (var pid in partIds)
                        participants.Add(await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(pid));
                    var membersByParticipant = await BuildMembersByParticipantMap(participants);

                    int teamSize = tournament.TeamSize ?? 1;
                    var rand = new Random();
                    foreach (var tm in affected)
                    {
                        foreach (var sm in tm.SubMatches ?? new List<MatchEntity>())
                            await this.AppUnitOfWork.MatchRepository.HardDeleteEntity(sm);
                        foreach (var sm in BuildSubMatchesForTeamMatch(tm, teamSize, null, membersByParticipant, rand))
                            await this.AppUnitOfWork.MatchRepository.AddEntity(sm, this.UserContextReader);
                        await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(tm, this.UserContextReader);
                    }
                }
                else
                {
                    // A bye is involved — re-seed from the swapped slots (rebuilds the whole knockout).
                    EnsureNoKnockoutResultPlayed(allMatches);
                    await RegenerateBracketWithSwapAsync(
                        tournament, tournamentId, knockoutStage, useDoubleKnockout,
                        BuildSlotIdsFromTeamFirstRound(firstRound), participantAId, participantBId);
                }
            }
            else
            {
                var allMatches = await this.AppUnitOfWork.MatchRepository.GetByStageId(knockoutStage.Id!.Value);
                var firstRound = allMatches.Where(m => (m.RoundNumber ?? 1) == 1).ToList();

                var (matchA, aIsHome) = LocateSoloSlot(firstRound, participantAId);
                var (matchB, bIsHome) = LocateSoloSlot(firstRound, participantBId);

                if (matchA != null && matchB != null)
                {
                    if (aIsHome) matchA.HomeParticipantId = participantBId; else matchA.AwayParticipantId = participantBId;
                    if (bIsHome) matchB.HomeParticipantId = participantAId; else matchB.AwayParticipantId = participantAId;
                    foreach (var m in new[] { matchA, matchB }.Distinct())
                        await this.AppUnitOfWork.MatchRepository.UpdateEntity(m, this.UserContextReader);
                }
                else
                {
                    EnsureNoKnockoutResultPlayed(allMatches);
                    await RegenerateBracketWithSwapAsync(
                        tournament, tournamentId, knockoutStage, useDoubleKnockout,
                        BuildSlotIdsFromSoloFirstRound(firstRound), participantAId, participantBId);
                }
            }

            // Keep each team's Seed travelling with it so the bracket labels stay consistent.
            await SwapParticipantSeedsAsync(participantAId, participantBId);

            await this.SaveAsync();
            await InvalidateTournamentBracketCaches(tournamentId);
        }

        // Rebuilds the whole knockout after exchanging two first-round slots — used when a bye is involved
        // and the surgical in-place swap can't apply. Order is preserved (no re-randomised seeding); only
        // the two named teams move. The bracket must be unplayed (caller checks).
        private async Task RegenerateBracketWithSwapAsync(
            TournamentEntity tournament, Guid tournamentId, TournamentStageEntity knockoutStage, bool useDoubleKnockout,
            Guid?[] slotIds, Guid aId, Guid bId)
        {
            int ia = -1, ib = -1;
            for (int i = 0; i < slotIds.Length; i++)
            {
                if (slotIds[i] == aId) ia = i;
                if (slotIds[i] == bId) ib = i;
            }
            if (ia < 0 || ib < 0)
                throw new BusinessRuleException("Both teams must be in the first round of the bracket.");

            (slotIds[ia], slotIds[ib]) = (slotIds[ib], slotIds[ia]);

            var byId = new Dictionary<Guid, TournamentParticipantEntity>();
            foreach (var pid in slotIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct())
                byId[pid] = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(pid);

            var slots = slotIds.Select(x => x.HasValue ? byId[x!.Value] : (TournamentParticipantEntity?)null).ToList();

            await TearDownKnockoutMatchesAsync(tournament, knockoutStage);
            await this.SaveAsync(); // commit the teardown before repopulating the same stage
            await PopulateKnockoutFromSlots(tournament, tournamentId, knockoutStage, useDoubleKnockout, slots, new Random());
        }

        // First-round slot ids (index 2*order = home, 2*order+1 = away; null = bye), reconstructed from
        // the seeded first round so a re-seed can rebuild the bracket from a swapped arrangement.
        private static Guid?[] BuildSlotIdsFromTeamFirstRound(List<TeamMatchEntity> firstRound)
        {
            var slots = new Guid?[firstRound.Count * 2];
            foreach (var tm in firstRound)
            {
                int o = tm.MatchOrder ?? 0;
                if (2 * o + 1 >= slots.Length) continue;
                slots[2 * o] = tm.HomeTeamParticipantId;
                slots[2 * o + 1] = tm.AwayTeamParticipantId;
            }
            return slots;
        }

        private static Guid?[] BuildSlotIdsFromSoloFirstRound(List<MatchEntity> firstRound)
        {
            var slots = new Guid?[firstRound.Count * 2];
            foreach (var m in firstRound)
            {
                int o = m.MatchOrder ?? 0;
                if (2 * o + 1 >= slots.Length) continue;
                slots[2 * o] = m.HomeParticipantId;
                slots[2 * o + 1] = m.AwayParticipantId;
            }
            return slots;
        }

        private static void EnsureNoKnockoutResultPlayed(List<TeamMatchEntity> matches)
        {
            if (matches.Any(tm => tm.Status == TeamMatchStatus.Completed
                    && tm.HomeTeamParticipantId.HasValue && tm.AwayTeamParticipantId.HasValue))
                throw new BusinessRuleException("A knockout match has already been played — reset the bracket to re-seed.");
        }

        private static void EnsureNoKnockoutResultPlayed(List<MatchEntity> matches)
        {
            if (matches.Any(m => m.Status == MatchStatus.Completed
                    && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue))
                throw new BusinessRuleException("A knockout match has already been played — reset the bracket to re-seed.");
        }

        // First-round fixture + side (true = home) that holds the participant, but only when it is a real,
        // unplayed match (both sides present, still pending). Byes and played matches return null.
        private static (TeamMatchEntity? Match, bool IsHome) LocateTeamSlot(List<TeamMatchEntity> firstRound, Guid participantId)
        {
            foreach (var tm in firstRound)
            {
                if (tm.Status != TeamMatchStatus.Pending) continue;
                if (!tm.HomeTeamParticipantId.HasValue || !tm.AwayTeamParticipantId.HasValue) continue;
                if (tm.HomeTeamParticipantId == participantId) return (tm, true);
                if (tm.AwayTeamParticipantId == participantId) return (tm, false);
            }
            return (null, false);
        }

        private static (MatchEntity? Match, bool IsHome) LocateSoloSlot(List<MatchEntity> firstRound, Guid participantId)
        {
            foreach (var m in firstRound)
            {
                if (m.Status != MatchStatus.Pending && m.Status != MatchStatus.Scheduled) continue;
                if (!m.HomeParticipantId.HasValue || !m.AwayParticipantId.HasValue) continue;
                if (m.HomeParticipantId == participantId) return (m, true);
                if (m.AwayParticipantId == participantId) return (m, false);
            }
            return (null, false);
        }

        private async Task SwapParticipantSeedsAsync(Guid aId, Guid bId)
        {
            var a = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(aId);
            var b = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(bId);
            (a.Seed, b.Seed) = (b.Seed, a.Seed);
            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(a, this.UserContextReader);
            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(b, this.UserContextReader);
        }

        // Hard-deletes every match in the knockout stage(s): the single-elim / WB stage plus, for
        // double-elimination, the losers bracket that sits at the next order. Returns the number of
        // fixtures removed so callers can tell an already-drawn bracket from an empty one.
        private async Task<int> TearDownKnockoutMatchesAsync(TournamentEntity tournament, TournamentStageEntity knockoutStage)
        {
            var stageIds = new List<Guid> { knockoutStage.Id!.Value };

            if (knockoutStage.Type == StageType.DoubleEliminationWinnersBracket)
            {
                var lbStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournament.Id!.Value, 3);
                if (lbStage != null && lbStage.Type == StageType.DoubleEliminationLosersBracket)
                    stageIds.Add(lbStage.Id!.Value);
            }

            int removed = 0;
            foreach (var stageId in stageIds)
            {
                if (tournament.IsTeamTournament)
                {
                    var teamMatches = await this.AppUnitOfWork.TeamMatchRepository.GetByStageId(stageId);
                    foreach (var tm in teamMatches)
                    {
                        if (tm.SubMatches != null)
                            foreach (var sm in tm.SubMatches)
                                await this.AppUnitOfWork.MatchRepository.HardDeleteEntity(sm);
                        await this.AppUnitOfWork.TeamMatchRepository.HardDeleteEntity(tm);
                        removed++;
                    }
                }
                else
                {
                    var matches = await this.AppUnitOfWork.MatchRepository.GetByStageId(stageId);
                    foreach (var m in matches)
                    {
                        await this.AppUnitOfWork.MatchRepository.HardDeleteEntity(m);
                        removed++;
                    }
                }
            }
            return removed;
        }

        private async Task InvalidateTournamentBracketCaches(Guid tournamentId)
        {
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{tournamentId}");
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveAsync($"pdf:bracket:{tournamentId}");
            await cacheService.RemoveAsync($"tournament:{tournamentId}");
        }

        private async Task GenerateBracketForFormat(TournamentEntity tournament, Guid tournamentId, TimeSpan? roundDuration)
        {
            switch (tournament.Format)
            {
                case TournamentFormat.SingleElimination:
                    if (tournament.IsTeamTournament)
                        await GenerateTeamSingleEliminationBracket(tournamentId);
                    else
                        await GenerateSingleEliminationBracket(tournamentId);
                    break;

                case TournamentFormat.League:
                    if (tournament.IsTeamTournament)
                        await GenerateTeamLeagueTournament(tournamentId, doubleRoundRobin: tournament.DoubleRoundRobin, roundDuration: roundDuration);
                    else
                        await GenerateLeagueTournament(tournamentId, doubleRoundRobin: tournament.DoubleRoundRobin, roundDuration: roundDuration);
                    break;

                case TournamentFormat.DoubleElimination:
                    if (tournament.IsTeamTournament)
                        await GenerateTeamDoubleEliminationBracket(tournamentId);
                    else
                        await GenerateDoubleEliminationBracket(tournamentId);
                    break;

                case TournamentFormat.GroupStageWithKnockout:
                    if (!tournament.GroupsCount.HasValue || !tournament.QualifiersPerGroup.HasValue)
                        throw new Exception("Group count and qualifiers count are required for this format.");
                    if (tournament.IsTeamTournament)
                        await GenerateTeamGroupStageWithKnockout(tournamentId, tournament.GroupsCount.Value, tournament.QualifiersPerGroup!.Value, roundDuration, tournament.DoubleRoundRobin);
                    else
                        await GenerateGroupStageWithKnockout(tournamentId, tournament.GroupsCount.Value, tournament.QualifiersPerGroup!.Value, roundDuration, tournament.DoubleRoundRobin);
                    break;

                case TournamentFormat.Swiss:
                    // Team Swiss is not wired up — round-by-round pairing would need TeamMatchEntity
                    // generation on every advance plus the team result pipeline. Solo only for now.
                    if (tournament.IsTeamTournament)
                        throw new Exception("Team Swiss tournaments are not supported yet.");
                    await GenerateSwissTournament(tournamentId, roundDuration);
                    break;

                default:
                    throw new Exception($"Tournament format {tournament.Format} not supported");
            }
        }

        #endregion 1. Tournament Generation Entry Points

        #region 2. Generators

        public async Task GenerateSingleEliminationBracket(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);

            var participants = tournament!.TournamentParticipants?.ToList();
            if (participants == null || participants.Count == 0)
                throw new Exception("No participants");

            var shuffledParticipants = participants.OrderBy(a => Guid.NewGuid()).ToList();

            for (int i = 0; i < shuffledParticipants.Count; i++)
            {
                shuffledParticipants[i].Seed = i + 1;
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(shuffledParticipants[i], this.UserContextReader);
            }

            int bracketSize = GetNextPowerOfTwo(shuffledParticipants.Count);
            var seedOrder = GetStandardSeedOrder(bracketSize);
            var participantsBySeed = shuffledParticipants
                .Where(p => p.Seed.HasValue)
                .ToDictionary(p => p.Seed!.Value, p => p);

            var bracketSlots = seedOrder
                .Select(seed => participantsBySeed.TryGetValue(seed, out var participant) ? participant : null)
                .ToList();

            var stage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.SingleEliminationBracket,
                Order = 1,
                Name = "Main Bracket"
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(stage, this.UserContextReader);

            var allMatches = GenerateEliminationMatches(tournamentId, stage.Id.Value, bracketSlots, tournament.HasThirdPlaceMatch);

            foreach (var match in allMatches)
            {
                await this.AppUnitOfWork.MatchRepository.AddEntity(match, this.UserContextReader);
            }

            await this.SaveAsync();
        }

        public async Task GenerateLeagueTournament(Guid tournamentId, bool doubleRoundRobin = false, TimeSpan? roundDuration = null)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);

            var participants = tournament!.TournamentParticipants?
                .OrderBy(x => Guid.NewGuid())
                .ToList();

            var stage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.League,
                Order = 1,
                Name = "League Season"
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(stage, this.UserContextReader);

            var group = new TournamentGroupEntity
            {
                Id = Guid.NewGuid(),
                TournamentStageId = stage.Id,
                Name = "League Table"
            };
            await this.AppUnitOfWork.TournamentGroupRepository.AddEntity(group, this.UserContextReader);

            foreach (var p in participants!)
            {
                p.TournamentGroupId = group.Id;
                p.Points = 0; p.Wins = 0; p.Draws = 0; p.Losses = 0; p.GoalsFor = 0; p.GoalsAgainst = 0;
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(p, this.UserContextReader);
            }

            var allMatches = GenerateRoundRobinMatches(tournamentId, stage.Id.Value, participants!, doubleRoundRobin);

            AssignAllRoundSchedules(allMatches, roundDuration);

            foreach (var match in allMatches)
            {
                match.TournamentGroupId = group.Id;
                await this.AppUnitOfWork.MatchRepository.AddEntity(match, this.UserContextReader);
            }

            await this.SaveAsync();
        }

        public async Task GenerateGroupStageWithKnockout(Guid tournamentId, int numberOfGroups, int qualifiersPerGroup, TimeSpan? roundDuration = null, bool doubleRoundRobin = false)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);
            var participants = tournament!.TournamentParticipants?.ToList();

            if (participants!.Count < numberOfGroups * 2)
                throw new Exception($"Not enough participants. Need at least {numberOfGroups * 2} players for {numberOfGroups} groups.");

            int totalQualifiers = numberOfGroups * qualifiersPerGroup;
            if (totalQualifiers < 2)
                throw new Exception("Need at least 2 qualifiers to build a knockout bracket.");
            // Both single- and double-elimination pad the bracket up to the next power of two with byes
            // (e.g. 6 qualifiers → bracket of 8, top 2 seeds on a bye), so any qualifier count works.

            var groupStage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.GroupStage,
                Order = 1,
                Name = "Group Stage",
                QualifiedPlayersCount = qualifiersPerGroup
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(groupStage, this.UserContextReader);

            var groups = new List<TournamentGroupEntity>();
            for (int i = 0; i < numberOfGroups; i++)
            {
                var group = new TournamentGroupEntity
                {
                    Id = Guid.NewGuid(),
                    TournamentStageId = groupStage.Id,
                    Name = $"Group {GetGroupName(i)}"
                };
                groups.Add(group);
                await this.AppUnitOfWork.TournamentGroupRepository.AddEntity(group, this.UserContextReader);
            }

            var seededParticipants = participants.OrderBy(p => p.Seed ?? 999).ToList();
            var groupParticipants = groups.ToDictionary(g => g.Id!.Value, _ => new List<TournamentParticipantEntity>());

            for (int i = 0; i < seededParticipants.Count; i++)
            {
                int groupIndex = (i / numberOfGroups) % 2 == 0
                    ? i % numberOfGroups
                    : numberOfGroups - 1 - (i % numberOfGroups);

                var participant = seededParticipants[i];
                var targetGroup = groups[groupIndex];

                participant.TournamentGroupId = targetGroup.Id;
                participant.Points = 0;
                participant.Wins = 0;
                participant.Draws = 0;
                participant.Losses = 0;
                participant.GoalsFor = 0;
                participant.GoalsAgainst = 0;

                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(participant, this.UserContextReader);
                groupParticipants[targetGroup.Id!.Value].Add(participant);
            }

            var allMatches = new List<MatchEntity>();
            foreach (var group in groups)
            {
                var participantsInGroup = groupParticipants[group.Id!.Value];

                if (participantsInGroup.Count < 2)
                    continue;

                var groupMatches = GenerateRoundRobinMatches(
                    tournamentId,
                    groupStage.Id.Value,
                    participantsInGroup,
                    doubleRoundRobin
                );

                foreach (var m in groupMatches)
                {
                    m.TournamentGroupId = group.Id;
                    m.Stage = MatchStage.GroupStage;
                    allMatches.Add(m);
                }
            }

            AssignAllRoundSchedules(allMatches, roundDuration);

            foreach (var match in allMatches)
            {
                await this.AppUnitOfWork.MatchRepository.AddEntity(match, this.UserContextReader);
            }

            // Knockout phase: single-elim by default, or Winners+Losers bracket stages when the
            // organizer chose double-elimination (solo only, and only when there are enough
            // qualifiers for a real losers bracket). Matches are filled in when the groups finish.
            bool useDoubleKnockout = UseDoubleEliminationKnockout(tournament) && totalQualifiers >= 4;
            await AddKnockoutStages(tournamentId, useDoubleKnockout, firstOrder: 2, qualifiedPlayersCount: totalQualifiers);

            await this.SaveAsync();
        }

        public async Task GenerateTeamLeagueTournament(Guid tournamentId, bool doubleRoundRobin = false, TimeSpan? roundDuration = null)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);

            if (!tournament!.TeamSize.HasValue)
                throw new Exception("TeamSize is required for team tournaments.");

            int teamSize = tournament.TeamSize.Value;

            var participants = tournament.TournamentParticipants?
                .OrderBy(x => Guid.NewGuid())
                .ToList();

            if (participants == null || participants.Count < 2)
                throw new Exception("Not enough team participants");

            var stage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.League,
                Order = 1,
                Name = "League Season"
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(stage, this.UserContextReader);

            var group = new TournamentGroupEntity
            {
                Id = Guid.NewGuid(),
                TournamentStageId = stage.Id,
                Name = "League Table"
            };
            await this.AppUnitOfWork.TournamentGroupRepository.AddEntity(group, this.UserContextReader);

            foreach (var p in participants)
            {
                p.TournamentGroupId = group.Id;
                p.Points = 0; p.Wins = 0; p.Draws = 0; p.Losses = 0; p.GoalsFor = 0; p.GoalsAgainst = 0;
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(p, this.UserContextReader);
            }

            var teamMatches = GenerateRoundRobinTeamMatches(tournamentId, stage.Id.Value, participants, doubleRoundRobin);

            foreach (var tm in teamMatches)
                await this.AppUnitOfWork.TeamMatchRepository.AddEntity(tm, this.UserContextReader);

            var membersByParticipant = await BuildMembersByParticipantMap(participants);
            var rand = new Random();

            var allSubMatches = new List<MatchEntity>();
            foreach (var tm in teamMatches)
            {
                var subs = BuildSubMatchesForTeamMatch(tm, teamSize, group.Id, membersByParticipant, rand);
                allSubMatches.AddRange(subs);
            }

            AssignAllRoundSchedules(allSubMatches, roundDuration);

            foreach (var sm in allSubMatches)
                await this.AppUnitOfWork.MatchRepository.AddEntity(sm, this.UserContextReader);

            await this.SaveAsync();
        }

        public async Task GenerateTeamGroupStageWithKnockout(Guid tournamentId, int numberOfGroups, int qualifiersPerGroup, TimeSpan? roundDuration = null, bool doubleRoundRobin = false)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);

            if (!tournament!.TeamSize.HasValue)
                throw new Exception("TeamSize is required for team tournaments.");

            int teamSize = tournament.TeamSize.Value;

            var participants = tournament.TournamentParticipants?.ToList();

            if (participants!.Count < numberOfGroups * 2)
                throw new Exception($"Not enough participants. Need at least {numberOfGroups * 2} teams for {numberOfGroups} groups.");

            int totalQualifiers = numberOfGroups * qualifiersPerGroup;
            if (totalQualifiers < 2)
                throw new Exception("Need at least 2 qualifiers to build a knockout bracket.");
            // Both single- and double-elimination pad the bracket up to the next power of two with byes
            // (e.g. 6 qualifiers → bracket of 8, top 2 seeds on a bye), so any qualifier count works.

            var groupStage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.GroupStage,
                Order = 1,
                Name = "Group Stage",
                QualifiedPlayersCount = qualifiersPerGroup
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(groupStage, this.UserContextReader);

            var groups = new List<TournamentGroupEntity>();
            for (int i = 0; i < numberOfGroups; i++)
            {
                var g = new TournamentGroupEntity
                {
                    Id = Guid.NewGuid(),
                    TournamentStageId = groupStage.Id,
                    Name = $"Group {GetGroupName(i)}"
                };
                groups.Add(g);
                await this.AppUnitOfWork.TournamentGroupRepository.AddEntity(g, this.UserContextReader);
            }

            var seededParticipants = participants.OrderBy(p => p.Seed ?? 999).ToList();
            var groupParticipants = groups.ToDictionary(g => g.Id!.Value, _ => new List<TournamentParticipantEntity>());

            for (int i = 0; i < seededParticipants.Count; i++)
            {
                int groupIndex = (i / numberOfGroups) % 2 == 0
                    ? i % numberOfGroups
                    : numberOfGroups - 1 - (i % numberOfGroups);

                var participant = seededParticipants[i];
                var targetGroup = groups[groupIndex];

                participant.TournamentGroupId = targetGroup.Id;
                participant.Points = 0;
                participant.Wins = 0;
                participant.Draws = 0;
                participant.Losses = 0;
                participant.GoalsFor = 0;
                participant.GoalsAgainst = 0;

                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(participant, this.UserContextReader);
                groupParticipants[targetGroup.Id!.Value].Add(participant);
            }

            var allTeamMatches = new List<TeamMatchEntity>();
            var allSubMatches = new List<MatchEntity>();
            var membersByParticipant = await BuildMembersByParticipantMap(participants);
            var rand = new Random();

            foreach (var g in groups)
            {
                var ps = groupParticipants[g.Id!.Value];
                if (ps.Count < 2) continue;

                var tms = GenerateRoundRobinTeamMatches(tournamentId, groupStage.Id.Value, ps, doubleRoundRobin);
                allTeamMatches.AddRange(tms);

                foreach (var tm in tms)
                {
                    var subs = BuildSubMatchesForTeamMatch(tm, teamSize, g.Id, membersByParticipant, rand);
                    allSubMatches.AddRange(subs);
                }
            }

            foreach (var tm in allTeamMatches)
                await this.AppUnitOfWork.TeamMatchRepository.AddEntity(tm, this.UserContextReader);

            AssignAllRoundSchedules(allSubMatches, roundDuration);

            foreach (var sm in allSubMatches)
                await this.AppUnitOfWork.MatchRepository.AddEntity(sm, this.UserContextReader);

            // Knockout phase: single-elim by default, or Winners+Losers bracket stages when the
            // organizer chose double-elimination and there are enough qualifiers for a real LB.
            // Matches are filled in by CheckAndAdvanceGroupStage once the groups finish.
            bool useDoubleKnockout = UseDoubleEliminationKnockout(tournament) && totalQualifiers >= 4;
            await AddKnockoutStages(tournamentId, useDoubleKnockout, firstOrder: 2, qualifiedPlayersCount: totalQualifiers);

            await this.SaveAsync();
        }

        public async Task GenerateDoubleEliminationBracket(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);
            var participants = tournament!.TournamentParticipants?.ToList();

            if (participants == null || participants.Count < 4)
                throw new Exception("Double elimination requires at least 4 participants.");

            // Solo-only generator. Team double-elimination has its own generator
            // (GenerateTeamDoubleEliminationBracket); GenerateBracketForFormat routes teams there.
            if (tournament.IsTeamTournament)
                throw new Exception("Use GenerateTeamDoubleEliminationBracket for team tournaments.");

            var shuffled = participants.OrderBy(_ => Guid.NewGuid()).ToList();
            for (int i = 0; i < shuffled.Count; i++)
            {
                shuffled[i].Seed = i + 1;
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(shuffled[i], this.UserContextReader);
            }

            // Non-power-of-two counts are padded up to the next power of two — the standard
            // bracket-seeding spread leaves the bye slots on the top-seed side so the byes
            // get auto-advanced in WB R1 and the LB cascade below collapses any LB match
            // that would otherwise have nothing to play.
            int bracketSize = GetNextPowerOfTwo(shuffled.Count);
            var seedOrder = GetStandardSeedOrder(bracketSize);
            var participantsBySeed = shuffled
                .Where(p => p.Seed.HasValue)
                .ToDictionary(p => p.Seed!.Value, p => p);
            var bracketSlots = seedOrder
                .Select(s => participantsBySeed.TryGetValue(s, out var p) ? p : null)
                .ToList();

            // Two stages: Winners + Losers. The Grand Final lives in the Winners Bracket stage
            // as a separate match flagged with MatchStage.GrandFinal so the structure endpoint
            // can return both rounds cleanly without a dedicated "GF stage".
            var wbStage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.DoubleEliminationWinnersBracket,
                Order = 1,
                Name = "Winners Bracket"
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(wbStage, this.UserContextReader);

            var lbStage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.DoubleEliminationLosersBracket,
                Order = 2,
                Name = "Losers Bracket"
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(lbStage, this.UserContextReader);

            var allMatches = GenerateDoubleEliminationMatches(
                tournamentId, wbStage.Id!.Value, lbStage.Id!.Value, bracketSlots);

            foreach (var match in allMatches)
                await this.AppUnitOfWork.MatchRepository.AddEntity(match, this.UserContextReader);

            await this.SaveAsync();
        }

        /// <summary>
        /// Team variant of <see cref="GenerateDoubleEliminationBracket"/>: same Winners/Losers
        /// bracket shape and Grand Final, but built on <see cref="TeamMatchEntity"/> with per-roster
        /// sub-matches created for every fixture that already has both teams.
        /// </summary>
        public async Task GenerateTeamDoubleEliminationBracket(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);
            var participants = tournament!.TournamentParticipants?.ToList();

            if (participants == null || participants.Count < 4)
                throw new Exception("Double elimination requires at least 4 participants.");

            if (!tournament.TeamSize.HasValue)
                throw new Exception("TeamSize is required for team tournaments.");

            int teamSize = tournament.TeamSize.Value;

            var shuffled = participants.OrderBy(_ => Guid.NewGuid()).ToList();
            for (int i = 0; i < shuffled.Count; i++)
            {
                shuffled[i].Seed = i + 1;
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(shuffled[i], this.UserContextReader);
            }

            int bracketSize = GetNextPowerOfTwo(shuffled.Count);
            var seedOrder = GetStandardSeedOrder(bracketSize);
            var participantsBySeed = shuffled
                .Where(p => p.Seed.HasValue)
                .ToDictionary(p => p.Seed!.Value, p => p);
            var bracketSlots = seedOrder
                .Select(s => participantsBySeed.TryGetValue(s, out var p) ? p : null)
                .ToList();

            // Two stages mirror the solo layout: Winners + Losers. The Grand Final lives in the
            // Winners stage flagged IsGrandFinal so the structure endpoint can pull it out cleanly.
            var wbStage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.DoubleEliminationWinnersBracket,
                Order = 1,
                Name = "Winners Bracket"
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(wbStage, this.UserContextReader);

            var lbStage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.DoubleEliminationLosersBracket,
                Order = 2,
                Name = "Losers Bracket"
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(lbStage, this.UserContextReader);

            var allTeamMatches = GenerateTeamDoubleEliminationMatches(
                tournamentId, wbStage.Id!.Value, lbStage.Id!.Value, bracketSlots);

            foreach (var tm in allTeamMatches)
                await this.AppUnitOfWork.TeamMatchRepository.AddEntity(tm, this.UserContextReader);

            // Sub-matches for every fixture that already has both teams (single batched member
            // lookup avoids per-match N+1) — same pattern as GenerateTeamSingleEliminationBracket.
            var membersByParticipant = await BuildMembersByParticipantMap(shuffled);
            var rand = new Random();

            foreach (var tm in allTeamMatches)
            {
                if (!tm.HomeTeamParticipantId.HasValue || !tm.AwayTeamParticipantId.HasValue)
                    continue;
                if (tm.Status == TeamMatchStatus.Completed)
                    continue;

                var subs = BuildSubMatchesForTeamMatch(tm, teamSize, null, membersByParticipant, rand);
                foreach (var sm in subs)
                    await this.AppUnitOfWork.MatchRepository.AddEntity(sm, this.UserContextReader);
            }

            await this.SaveAsync();
        }

        /// <summary>
        /// Builds a double-elimination bracket: Winners Bracket (single-elim tree), Losers Bracket
        /// (with WB losers dropping in via reverse placement to avoid early rematches), and a
        /// single-match Grand Final fed by the WB champion (home) and the LB champion (away).
        /// </summary>
        /// <remarks>
        /// LB shape for bracket size C = 2^k (k ≥ 2):
        ///   • LB Round 1 (major, takes WB R1 losers):           C/4 matches
        ///   • LB Round 2 (major, takes WB R2 losers + LB R1):   C/4 matches
        ///   • LB Round 3 (minor, LB R2 winners):                C/8 matches
        ///   • LB Round 4 (major, takes WB R3 losers + LB R3):   C/8 matches
        ///   • ... alternating minor/major down to a single LB Final fed by the WB Final loser.
        /// Total LB rounds = 2(k-1).
        ///
        /// Non-power-of-two participant counts pad up to bracket size 2^k with bye slots in
        /// WB R1. WB R1 byes auto-advance their lone participant to WB R2 (no loser produced),
        /// and any LB match that would otherwise have nothing to play (both upstream WB sources
        /// were byes) is collapsed: its single live upstream is re-routed to skip past it, so
        /// at runtime the bracket plays without spurious 1-player matches.
        /// </remarks>
        private List<MatchEntity> GenerateDoubleEliminationMatches(
            Guid tournamentId, Guid wbStageId, Guid lbStageId,
            List<TournamentParticipantEntity?> bracketSlots)
        {
            int bracketSize = bracketSlots.Count;
            int wbRounds = (int)Math.Log2(bracketSize);

            // -- Winners Bracket --
            var wbByRound = new Dictionary<int, List<MatchEntity>>();
            int matchesInRound = bracketSize / 2;

            var firstRoundWb = new List<MatchEntity>();
            for (int i = 0; i < matchesInRound; i++)
            {
                var m = CreateMatch(tournamentId, wbStageId, 1, GetMatchStage(bracketSize, 1), i);
                m.HomeParticipantId = bracketSlots[i * 2]?.Id;
                m.AwayParticipantId = bracketSlots[i * 2 + 1]?.Id;
                firstRoundWb.Add(m);
            }
            wbByRound[1] = firstRoundWb;

            var current = firstRoundWb;
            for (int r = 2; r <= wbRounds; r++)
            {
                matchesInRound /= 2;
                var next = new List<MatchEntity>();
                for (int i = 0; i < matchesInRound; i++)
                {
                    var m = CreateMatch(tournamentId, wbStageId, r, GetMatchStage(bracketSize, r), i);
                    current[i * 2].NextMatchId = m.Id;
                    current[i * 2 + 1].NextMatchId = m.Id;
                    next.Add(m);
                }
                wbByRound[r] = next;
                current = next;
            }

            // -- Losers Bracket --
            int lbRoundCount = 2 * (wbRounds - 1);
            var lbByRound = new Dictionary<int, List<MatchEntity>>();
            for (int l = 1; l <= lbRoundCount; l++)
            {
                int count = LosersBracketMatchCount(bracketSize, l);
                var list = new List<MatchEntity>();
                for (int i = 0; i < count; i++)
                {
                    var m = CreateMatch(tournamentId, lbStageId, l, MatchStage.LosersBracket, i);
                    m.IsUpperBracket = false;
                    list.Add(m);
                }
                lbByRound[l] = list;
            }

            // Wire LB → LB forward links.
            for (int l = 1; l < lbRoundCount; l++)
            {
                var currentLb = lbByRound[l];
                var nextLb = lbByRound[l + 1];

                if (nextLb.Count == currentLb.Count)
                {
                    // Same-count step: previous round is the minor consolidation feeding a
                    // major round (or LB R1 → LB R2 at the start). Winner takes home; the WB
                    // drop fills away later.
                    for (int i = 0; i < currentLb.Count; i++)
                    {
                        currentLb[i].NextMatchId = nextLb[i].Id;
                        currentLb[i].NextMatchHomeAwaySlot = 0;
                    }
                }
                else
                {
                    // Halving step: a major round feeds the next minor round — matches pair up
                    // in the standard bracket-pair convention.
                    for (int i = 0; i < currentLb.Count; i++)
                    {
                        currentLb[i].NextMatchId = nextLb[i / 2].Id;
                        currentLb[i].NextMatchHomeAwaySlot = i % 2;
                    }
                }
            }

            // Wire WB losers → LB. WB R1 losers pair into LB R1 in the natural order
            // (no rematch risk since they just played each other). All later WB rounds use
            // REVERSE placement into the matching LB round — the seed-1 side of WB is sent to
            // the seed-N side of LB so a WB-R(r) loser cannot face the player who just sent
            // them down without first surviving the LB.
            var wbR1 = wbByRound[1];
            var lbR1 = lbByRound[1];
            for (int i = 0; i < wbR1.Count; i++)
            {
                wbR1[i].NextMatchLoserBracketId = lbR1[i / 2].Id;
                wbR1[i].NextMatchLoserBracketHomeAwaySlot = i % 2;
            }

            for (int r = 2; r <= wbRounds; r++)
            {
                int targetLbRound = 2 * r - 2;
                var wbRound = wbByRound[r];
                var lbRound = lbByRound[targetLbRound];
                int count = lbRound.Count; // matches WB round count by construction

                for (int i = 0; i < count; i++)
                {
                    wbRound[i].NextMatchLoserBracketId = lbRound[count - 1 - i].Id;
                    wbRound[i].NextMatchLoserBracketHomeAwaySlot = 1; // away — LB winner already holds home
                }
            }

            // -- Grand Final --
            // Lives in the WB stage but flagged so the UI can pull it out and render separately.
            // WB champion → home, LB champion → away.
            var grandFinal = CreateMatch(tournamentId, wbStageId, wbRounds + 1, MatchStage.GrandFinal, 0);

            var wbFinal = wbByRound[wbRounds].Single();
            wbFinal.NextMatchId = grandFinal.Id;
            wbFinal.NextMatchHomeAwaySlot = 0;

            var lbFinal = lbByRound[lbRoundCount].Single();
            lbFinal.NextMatchId = grandFinal.Id;
            lbFinal.NextMatchHomeAwaySlot = 1;

            var allMatches = new List<MatchEntity>();
            foreach (var list in wbByRound.Values) allMatches.AddRange(list);
            foreach (var list in lbByRound.Values) allMatches.AddRange(list);
            allMatches.Add(grandFinal);

            // WB R1 byes: auto-advance the lone participant to WB R2 (no loser is produced —
            // the matching LB R1 slot stays empty and gets handled by the cascade below).
            // We scope the auto-advance to WB matches because the shared AutoAdvanceByes()
            // helper matches by RoundNumber and would otherwise mark every freshly-created
            // LB R1 match (also RoundNumber=1, both slots null) as Completed prematurely.
            var wbMatchesList = wbByRound.Values.SelectMany(x => x).Concat(new[] { grandFinal }).ToList();
            AutoAdvanceByes(wbMatchesList);

            // LB cascade: bypass any LB match whose upstream feeders won't both deliver a
            // participant. 0 live sources → mark it Completed (empty); 1 live source → re-route
            // that source's downstream pointer to skip this match. This keeps the runtime
            // advance logic free of bye-special-casing — every match that's still Pending after
            // generation is guaranteed to receive both participants once its feeders resolve.
            CollapseLbByeCascade(allMatches, lbByRound);

            return allMatches;
        }

        private static void CollapseLbByeCascade(
            List<MatchEntity> allMatches,
            Dictionary<int, List<MatchEntity>> lbByRound)
        {
            foreach (var lbRound in lbByRound.Keys.OrderBy(k => k))
            {
                foreach (var lbMatch in lbByRound[lbRound].OrderBy(m => m.MatchOrder))
                {
                    if (lbMatch.Status == MatchStatus.Completed) continue;

                    var liveSources = new List<(MatchEntity Src, bool WinnerEdge)>();
                    foreach (var s in allMatches)
                    {
                        if (s.NextMatchId == lbMatch.Id && SourceProducesWinner(s))
                            liveSources.Add((s, true));
                        if (s.NextMatchLoserBracketId == lbMatch.Id && SourceProducesLoser(s))
                            liveSources.Add((s, false));
                    }

                    if (liveSources.Count >= 2)
                        continue;

                    if (liveSources.Count == 0)
                    {
                        // Both upstream feeders are dead — nothing to play, never will play.
                        lbMatch.Status = MatchStatus.Completed;
                        continue;
                    }

                    // Exactly one live source — re-route it past lbMatch so the participant
                    // lands directly in lbMatch's downstream slot, preserving the bracket shape
                    // for everything beyond this point.
                    var (src, winnerEdge) = liveSources[0];
                    if (winnerEdge)
                    {
                        src.NextMatchId = lbMatch.NextMatchId;
                        src.NextMatchHomeAwaySlot = lbMatch.NextMatchHomeAwaySlot;
                    }
                    else
                    {
                        src.NextMatchLoserBracketId = lbMatch.NextMatchId;
                        src.NextMatchLoserBracketHomeAwaySlot = lbMatch.NextMatchHomeAwaySlot;
                    }
                    lbMatch.Status = MatchStatus.Completed;
                }
            }
        }

        // A "live" winner source is any match that will eventually have a winner — either a
        // bye that already has one (Status=Completed with WinnerParticipantId) or a Pending
        // match (which by construction will be played and yield a winner).
        private static bool SourceProducesWinner(MatchEntity src) =>
            src.Status != MatchStatus.Completed || src.WinnerParticipantId.HasValue;

        // A "live" loser source is any Pending match (Completed ones are either byes — which
        // don't produce a loser — or already-collapsed LB matches, which produce nothing).
        // WB R(≥2) matches are always Pending at generation time and always end up with two
        // participants once R(r-1) winners advance, so they're guaranteed loser producers.
        private static bool SourceProducesLoser(MatchEntity src) =>
            src.Status != MatchStatus.Completed;

        // Match count for LB round l with bracket size C: pairs of rounds share a count.
        //   l=1,2 → C/4 ; l=3,4 → C/8 ; l=5,6 → C/16 ; …
        private static int LosersBracketMatchCount(int bracketSize, int lbRound)
        {
            int exponent = ((lbRound - 1) / 2) + 2;
            return Math.Max(1, bracketSize >> exponent);
        }

        /// <summary>
        /// Team variant of <see cref="GenerateDoubleEliminationMatches"/> — identical bracket shape
        /// and routing, expressed with <see cref="TeamMatchEntity"/> links (NextTeamMatch* + slot
        /// overrides) and the IsUpperBracket / IsGrandFinal flags. See that method's remarks for the
        /// LB round structure and the bye-collapse rationale.
        /// </summary>
        private List<TeamMatchEntity> GenerateTeamDoubleEliminationMatches(
            Guid tournamentId, Guid wbStageId, Guid lbStageId,
            List<TournamentParticipantEntity?> bracketSlots)
        {
            int bracketSize = bracketSlots.Count;
            int wbRounds = (int)Math.Log2(bracketSize);

            TeamMatchEntity NewTeamMatch(Guid stageId, int round, int order, bool isUpper) => new()
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                TournamentStageId = stageId,
                RoundNumber = round,
                MatchOrder = order,
                Status = TeamMatchStatus.Pending,
                IsUpperBracket = isUpper
            };

            // -- Winners Bracket --
            var wbByRound = new Dictionary<int, List<TeamMatchEntity>>();
            int matchesInRound = bracketSize / 2;

            var firstRoundWb = new List<TeamMatchEntity>();
            for (int i = 0; i < matchesInRound; i++)
            {
                var m = NewTeamMatch(wbStageId, 1, i, true);
                m.HomeTeamParticipantId = bracketSlots[i * 2]?.Id;
                m.AwayTeamParticipantId = bracketSlots[i * 2 + 1]?.Id;
                firstRoundWb.Add(m);
            }
            wbByRound[1] = firstRoundWb;

            var current = firstRoundWb;
            for (int r = 2; r <= wbRounds; r++)
            {
                matchesInRound /= 2;
                var next = new List<TeamMatchEntity>();
                for (int i = 0; i < matchesInRound; i++)
                {
                    var m = NewTeamMatch(wbStageId, r, i, true);
                    current[i * 2].NextTeamMatchId = m.Id;
                    current[i * 2 + 1].NextTeamMatchId = m.Id;
                    next.Add(m);
                }
                wbByRound[r] = next;
                current = next;
            }

            // -- Losers Bracket --
            int lbRoundCount = 2 * (wbRounds - 1);
            var lbByRound = new Dictionary<int, List<TeamMatchEntity>>();
            for (int l = 1; l <= lbRoundCount; l++)
            {
                int count = LosersBracketMatchCount(bracketSize, l);
                var list = new List<TeamMatchEntity>();
                for (int i = 0; i < count; i++)
                    list.Add(NewTeamMatch(lbStageId, l, i, false));
                lbByRound[l] = list;
            }

            // Wire LB → LB forward links (same-count step: minor → major, winner takes home; the
            // WB drop fills away later. Halving step: major → minor, standard bracket-pair).
            for (int l = 1; l < lbRoundCount; l++)
            {
                var currentLb = lbByRound[l];
                var nextLb = lbByRound[l + 1];

                if (nextLb.Count == currentLb.Count)
                {
                    for (int i = 0; i < currentLb.Count; i++)
                    {
                        currentLb[i].NextTeamMatchId = nextLb[i].Id;
                        currentLb[i].NextTeamMatchHomeAwaySlot = 0;
                    }
                }
                else
                {
                    for (int i = 0; i < currentLb.Count; i++)
                    {
                        currentLb[i].NextTeamMatchId = nextLb[i / 2].Id;
                        currentLb[i].NextTeamMatchHomeAwaySlot = i % 2;
                    }
                }
            }

            // Wire WB losers → LB. WB R1 losers pair naturally into LB R1; later WB rounds use
            // REVERSE placement into the away slot (LB winner already holds home).
            var wbR1 = wbByRound[1];
            var lbR1 = lbByRound[1];
            for (int i = 0; i < wbR1.Count; i++)
            {
                wbR1[i].NextTeamMatchLoserBracketId = lbR1[i / 2].Id;
                wbR1[i].NextTeamMatchLoserBracketHomeAwaySlot = i % 2;
            }

            for (int r = 2; r <= wbRounds; r++)
            {
                int targetLbRound = 2 * r - 2;
                var wbRound = wbByRound[r];
                var lbRound = lbByRound[targetLbRound];
                int count = lbRound.Count; // matches WB round count by construction

                for (int i = 0; i < count; i++)
                {
                    wbRound[i].NextTeamMatchLoserBracketId = lbRound[count - 1 - i].Id;
                    wbRound[i].NextTeamMatchLoserBracketHomeAwaySlot = 1; // away — LB winner holds home
                }
            }

            // -- Grand Final -- lives in the WB stage, flagged so the UI renders it separately.
            var grandFinal = NewTeamMatch(wbStageId, wbRounds + 1, 0, true);
            grandFinal.IsGrandFinal = true;

            var wbFinal = wbByRound[wbRounds].Single();
            wbFinal.NextTeamMatchId = grandFinal.Id;
            wbFinal.NextTeamMatchHomeAwaySlot = 0;

            var lbFinal = lbByRound[lbRoundCount].Single();
            lbFinal.NextTeamMatchId = grandFinal.Id;
            lbFinal.NextTeamMatchHomeAwaySlot = 1;

            var allMatches = new List<TeamMatchEntity>();
            foreach (var list in wbByRound.Values) allMatches.AddRange(list);
            foreach (var list in lbByRound.Values) allMatches.AddRange(list);
            allMatches.Add(grandFinal);

            // WB R1 byes: auto-advance the lone team to WB R2. Scope to WB matches so the shared
            // AutoAdvanceTeamByes (matches by RoundNumber==1) doesn't mark fresh LB R1 as Completed.
            var wbMatchesList = wbByRound.Values.SelectMany(x => x).Concat(new[] { grandFinal }).ToList();
            AutoAdvanceTeamByes(wbMatchesList);

            // LB cascade: bypass any LB match whose upstream feeders won't both deliver a team.
            CollapseLbTeamByeCascade(allMatches, lbByRound);

            return allMatches;
        }

        // Team mirror of CollapseLbByeCascade: 0 live sources → mark Completed (empty); 1 live
        // source → re-route its downstream pointer to skip this match, preserving bracket shape.
        private static void CollapseLbTeamByeCascade(
            List<TeamMatchEntity> allMatches,
            Dictionary<int, List<TeamMatchEntity>> lbByRound)
        {
            foreach (var lbRound in lbByRound.Keys.OrderBy(k => k))
            {
                foreach (var lbMatch in lbByRound[lbRound].OrderBy(m => m.MatchOrder))
                {
                    if (lbMatch.Status == TeamMatchStatus.Completed) continue;

                    var liveSources = new List<(TeamMatchEntity Src, bool WinnerEdge)>();
                    foreach (var s in allMatches)
                    {
                        if (s.NextTeamMatchId == lbMatch.Id && TeamSourceProducesWinner(s))
                            liveSources.Add((s, true));
                        if (s.NextTeamMatchLoserBracketId == lbMatch.Id && TeamSourceProducesLoser(s))
                            liveSources.Add((s, false));
                    }

                    if (liveSources.Count >= 2)
                        continue;

                    if (liveSources.Count == 0)
                    {
                        lbMatch.Status = TeamMatchStatus.Completed;
                        continue;
                    }

                    var (src, winnerEdge) = liveSources[0];
                    if (winnerEdge)
                    {
                        src.NextTeamMatchId = lbMatch.NextTeamMatchId;
                        src.NextTeamMatchHomeAwaySlot = lbMatch.NextTeamMatchHomeAwaySlot;
                    }
                    else
                    {
                        src.NextTeamMatchLoserBracketId = lbMatch.NextTeamMatchId;
                        src.NextTeamMatchLoserBracketHomeAwaySlot = lbMatch.NextTeamMatchHomeAwaySlot;
                    }
                    lbMatch.Status = TeamMatchStatus.Completed;
                }
            }
        }

        private static bool TeamSourceProducesWinner(TeamMatchEntity src) =>
            src.Status != TeamMatchStatus.Completed || src.WinnerTeamParticipantId.HasValue;

        private static bool TeamSourceProducesLoser(TeamMatchEntity src) =>
            src.Status != TeamMatchStatus.Completed;

        /// <summary>
        /// Swiss format: everyone plays every round against an opponent on a similar score,
        /// nobody is eliminated. Rounds are paired lazily — each next round is generated by
        /// <see cref="CheckAndAdvanceSwissStage"/> only once the current one fully completes,
        /// because its matchups depend on the standings. Odd participant counts give one player
        /// per round a bye (a pre-completed free win, stored as a match with no away side).
        /// </summary>
        public async Task GenerateSwissTournament(Guid tournamentId, TimeSpan? roundDuration = null)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);

            var participants = tournament!.TournamentParticipants?.ToList();
            if (participants == null || participants.Count < 2)
                throw new Exception("Not enough participants");

            // Optional post-Swiss knockout: validate against the real participant count now,
            // at generation time (mirrors how GroupStageWithKnockout validates its config).
            var (knockoutSize, directBerths) = GetSwissKnockoutConfig(tournament);
            if (knockoutSize.HasValue)
            {
                int n = participants.Count;
                int size = knockoutSize.Value;
                int direct = directBerths!.Value;

                if (size < 2 || !IsPowerOfTwo(size))
                    throw new Exception("Knockout qualifiers must be a power of 2 (2, 4, 8, 16, 32).");
                if (size > n)
                    throw new Exception($"Knockout qualifiers ({size}) cannot exceed the participant count ({n}).");
                if (direct < 0 || direct > size)
                    throw new Exception($"Direct qualifiers must be between 0 and {size}.");
                if (direct < size && direct + 2 * (size - direct) > n)
                    throw new Exception(
                        $"Not enough participants for the play-in: {direct} direct + {2 * (size - direct)} play-in players need {direct + 2 * (size - direct)}, but only {n} registered.");
            }

            var stage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.Swiss,
                Order = 1,
                Name = "Swiss Rounds"
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(stage, this.UserContextReader);

            // Pre-create the post-Swiss stages (empty, filled when the Swiss rounds finish) so
            // the client shows the full tournament shape from day one — same pattern as the
            // knockout stage in GenerateGroupStageWithKnockout.
            if (knockoutSize.HasValue)
            {
                int nextOrder = 2;
                if (directBerths!.Value < knockoutSize.Value)
                {
                    await this.AppUnitOfWork.TournamentStageRepository.AddEntity(new TournamentStageEntity
                    {
                        Id = Guid.NewGuid(),
                        TournamentId = tournamentId,
                        Type = StageType.PlayIn,
                        Order = nextOrder++,
                        Name = "Play-In",
                        QualifiedPlayersCount = knockoutSize.Value - directBerths.Value
                    }, this.UserContextReader);
                }

                // Single-elim by default, or Winners+Losers bracket stages for double-elimination
                // (solo only — Swiss is always solo — and only with >= 4 qualifiers).
                bool useDoubleKnockout = UseDoubleEliminationKnockout(tournament) && knockoutSize.Value >= 4;
                await AddKnockoutStages(tournamentId, useDoubleKnockout, firstOrder: nextOrder, qualifiedPlayersCount: knockoutSize.Value);
            }

            // Single group so the league-style standings/resync machinery applies as-is.
            var group = new TournamentGroupEntity
            {
                Id = Guid.NewGuid(),
                TournamentStageId = stage.Id,
                Name = "Standings"
            };
            await this.AppUnitOfWork.TournamentGroupRepository.AddEntity(group, this.UserContextReader);

            // Random order doubles as the round-1 pairing order and as the last-resort
            // standings tiebreaker (Seed) for later rounds.
            var shuffled = participants.OrderBy(_ => Guid.NewGuid()).ToList();
            for (int i = 0; i < shuffled.Count; i++)
            {
                var p = shuffled[i];
                p.Seed = i + 1;
                p.TournamentGroupId = group.Id;
                p.Points = 0; p.Wins = 0; p.Draws = 0; p.Losses = 0; p.GoalsFor = 0; p.GoalsAgainst = 0;
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(p, this.UserContextReader);
            }

            var (matches, byeParticipant) = BuildSwissRoundMatches(
                tournamentId,
                stage.Id!.Value,
                group.Id,
                roundNumber: 1,
                orderedParticipants: shuffled,
                playedPairs: new HashSet<(Guid, Guid)>(),
                byeCounts: new Dictionary<Guid, int>(),
                roundDuration);

            foreach (var m in matches)
                await this.AppUnitOfWork.MatchRepository.AddEntity(m, this.UserContextReader);

            // Persist the eager bye credit explicitly — same pattern as CheckAndAdvanceSwissStage.
            // The participant is already EF-tracked here, but the explicit call documents intent
            // and keeps the two round-paths symmetric.
            if (byeParticipant != null)
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(byeParticipant, this.UserContextReader);

            await this.SaveAsync();
        }

        /// <summary>
        /// Builds the matches for one Swiss round from a standings-ordered pool: best-vs-next
        /// pairing that avoids rematches (backtracking), with plain adjacent pairing as the
        /// fallback when the played-graph admits no rematch-free matching. With an odd pool the
        /// lowest-ranked player among those with the fewest byes sits out and banks a free win
        /// (3 points, no goals) — applied to the participant entity immediately so standings are
        /// fresh, and re-derivable from the bye match row by the stats resync.
        /// </summary>
        private (List<MatchEntity> Matches, TournamentParticipantEntity? ByeParticipant) BuildSwissRoundMatches(
            Guid tournamentId,
            Guid stageId,
            Guid? groupId,
            int roundNumber,
            List<TournamentParticipantEntity> orderedParticipants,
            HashSet<(Guid, Guid)> playedPairs,
            Dictionary<Guid, int> byeCounts,
            TimeSpan? roundDuration)
        {
            var pool = new List<TournamentParticipantEntity>(orderedParticipants);

            TournamentParticipantEntity? byeParticipant = null;
            if (pool.Count % 2 != 0)
            {
                int fewestByes = pool.Min(p => byeCounts.GetValueOrDefault(p.Id!.Value));
                byeParticipant = Enumerable.Reverse(pool)
                    .First(p => byeCounts.GetValueOrDefault(p.Id!.Value) == fewestByes);
                pool.Remove(byeParticipant);
            }

            var pairs = PairSwissPlayers(pool, playedPairs);

            DateTime openAt = DateTime.UtcNow;
            DateTime? deadline = roundDuration.HasValue ? openAt + roundDuration.Value : null;

            var matches = new List<MatchEntity>();
            int order = 0;
            foreach (var (home, away) in pairs)
            {
                var m = CreateMatch(tournamentId, stageId, roundNumber, MatchStage.GroupStage, order++);
                m.TournamentGroupId = groupId;
                m.HomeParticipantId = home.Id;
                m.AwayParticipantId = away.Id;
                m.RoundOpenAt = openAt;
                m.RoundDeadline = deadline;
                matches.Add(m);
            }

            if (byeParticipant != null)
            {
                var bye = CreateMatch(tournamentId, stageId, roundNumber, MatchStage.GroupStage, order);
                bye.TournamentGroupId = groupId;
                bye.HomeParticipantId = byeParticipant.Id;
                bye.WinnerParticipantId = byeParticipant.Id;
                bye.Status = MatchStatus.Completed;
                bye.RoundOpenAt = openAt;
                bye.RoundDeadline = deadline;
                matches.Add(bye);

                // Mirror of the bye branch in ApplyMatchStats — keep the two in sync.
                byeParticipant.Wins++;
                byeParticipant.Points += 3;
            }

            return (matches, byeParticipant);
        }

        private static List<(TournamentParticipantEntity Home, TournamentParticipantEntity Away)> PairSwissPlayers(
            List<TournamentParticipantEntity> orderedPool,
            HashSet<(Guid, Guid)> playedPairs)
        {
            var result = new List<(TournamentParticipantEntity, TournamentParticipantEntity)>();
            int probeBudget = 200_000;
            if (TryPairAvoidingRematches(orderedPool, playedPairs, result, ref probeBudget))
                return result;

            // No rematch-free perfect matching (dense played-graph in late rounds, or the
            // probe budget ran out) — fall back to adjacent pairing and accept rematches.
            result.Clear();
            for (int i = 0; i + 1 < orderedPool.Count; i += 2)
                result.Add((orderedPool[i], orderedPool[i + 1]));
            return result;
        }

        // Depth-first search over pairings: the top remaining player tries opponents in
        // standings order, recursing on the rest. The probe budget bounds worst-case time;
        // exhausting it reports failure and lets the caller fall back.
        private static bool TryPairAvoidingRematches(
            List<TournamentParticipantEntity> pool,
            HashSet<(Guid, Guid)> playedPairs,
            List<(TournamentParticipantEntity, TournamentParticipantEntity)> result,
            ref int probeBudget)
        {
            if (pool.Count == 0) return true;
            if (--probeBudget <= 0) return false;

            var first = pool[0];
            for (int i = 1; i < pool.Count; i++)
            {
                var candidate = pool[i];
                if (HasPlayed(playedPairs, first.Id!.Value, candidate.Id!.Value)) continue;

                var rest = new List<TournamentParticipantEntity>(pool);
                rest.RemoveAt(i);
                rest.RemoveAt(0);

                result.Add((first, candidate));
                if (TryPairAvoidingRematches(rest, playedPairs, result, ref probeBudget)) return true;
                result.RemoveAt(result.Count - 1);
            }

            return false;
        }

        private static bool HasPlayed(HashSet<(Guid, Guid)> playedPairs, Guid a, Guid b)
            => playedPairs.Contains((a, b)) || playedPairs.Contains((b, a));

        // Normalised knockout config: (null, null) = pure Swiss; otherwise bracket size N plus
        // direct berths D, where D == N means every slot is a direct berth (no play-in).
        private static (int? KnockoutSize, int? DirectBerths) GetSwissKnockoutConfig(TournamentEntity tournament)
        {
            if (!tournament.SwissKnockoutQualifiers.HasValue || tournament.SwissKnockoutQualifiers.Value <= 0)
                return (null, null);

            int size = tournament.SwissKnockoutQualifiers.Value;
            int direct = tournament.SwissDirectQualifiers ?? size;
            return (size, Math.Min(direct, size));
        }

        // The post-group / post-swiss knockout phase runs as double-elimination when the organizer
        // opted in. Callers additionally require a bracket of >= 4 so a real losers bracket exists.
        // (Team Swiss is unsupported, so for teams this only ever applies to the group-stage path.)
        private static bool UseDoubleEliminationKnockout(TournamentEntity tournament)
            => tournament.KnockoutEliminationType == KnockoutEliminationType.Double;

        // Creates the knockout-phase stage(s) up-front (matches are filled in once the feeding
        // stage finishes). Double-elimination needs two stages — Winners + Losers brackets — laid
        // out at consecutive orders; single-elimination is one stage. The created stage type is
        // the single source of truth the population code later reads to pick the bracket shape.
        private async Task AddKnockoutStages(Guid tournamentId, bool useDouble, int firstOrder, int qualifiedPlayersCount)
        {
            if (useDouble)
            {
                await this.AppUnitOfWork.TournamentStageRepository.AddEntity(new TournamentStageEntity
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournamentId,
                    Type = StageType.DoubleEliminationWinnersBracket,
                    Order = firstOrder,
                    Name = "Winners Bracket",
                    QualifiedPlayersCount = qualifiedPlayersCount
                }, this.UserContextReader);

                await this.AppUnitOfWork.TournamentStageRepository.AddEntity(new TournamentStageEntity
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournamentId,
                    Type = StageType.DoubleEliminationLosersBracket,
                    Order = firstOrder + 1,
                    Name = "Losers Bracket"
                }, this.UserContextReader);
            }
            else
            {
                await this.AppUnitOfWork.TournamentStageRepository.AddEntity(new TournamentStageEntity
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournamentId,
                    Type = StageType.SingleEliminationBracket,
                    Order = firstOrder,
                    Name = "Knockout Stage",
                    QualifiedPlayersCount = qualifiedPlayersCount
                }, this.UserContextReader);
            }
        }

        // Effective Swiss round count: organizer override (clamped) or ceil(log2(n)) — the
        // smallest count that can isolate a single perfect-score winner.
        private static int GetSwissTotalRounds(int participantCount, int? configuredRounds)
        {
            if (participantCount < 2) return 0;

            // With rematch avoidance the schedule cannot exceed a full round robin:
            // n-1 rounds for even n, n for odd n (one bye per round).
            int maxRounds = participantCount % 2 == 0 ? participantCount - 1 : participantCount;

            int rounds = configuredRounds ?? (int)Math.Ceiling(Math.Log2(participantCount));
            return Math.Clamp(rounds, 1, maxRounds);
        }

        public async Task GenerateTeamSingleEliminationBracket(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);
            var participants = tournament!.TournamentParticipants?.ToList();

            if (participants == null || participants.Count < 2)
                throw new Exception("Not enough team participants");

            if (!tournament.TeamSize.HasValue)
                throw new Exception("TeamSize is required for team tournaments.");

            int teamSize = tournament.TeamSize.Value;

            // Shuffle and seed
            var shuffled = participants.OrderBy(a => Guid.NewGuid()).ToList();
            for (int i = 0; i < shuffled.Count; i++)
            {
                shuffled[i].Seed = i + 1;
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(shuffled[i], this.UserContextReader);
            }

            int bracketSize = GetNextPowerOfTwo(shuffled.Count);
            var seedOrder = GetStandardSeedOrder(bracketSize);
            var participantsBySeed = shuffled
                .Where(p => p.Seed.HasValue)
                .ToDictionary(p => p.Seed!.Value, p => p);

            var bracketSlots = seedOrder
                .Select(seed => participantsBySeed.TryGetValue(seed, out var participant) ? participant : null)
                .ToList();

            var stage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.SingleEliminationBracket,
                Order = 1,
                Name = "Main Bracket"
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(stage, this.UserContextReader);

            // Generate TeamMatchEntity bracket tree
            int playerCount = bracketSlots.Count;
            int totalRounds = (int)Math.Log2(playerCount);
            var allTeamMatches = new List<TeamMatchEntity>();
            var currentRoundTeamMatches = new List<TeamMatchEntity>();
            int matchesInRound = playerCount / 2;

            for (int i = 0; i < matchesInRound; i++)
            {
                var tm = new TeamMatchEntity
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournamentId,
                    TournamentStageId = stage.Id,
                    HomeTeamParticipantId = bracketSlots[i * 2]?.Id,
                    AwayTeamParticipantId = bracketSlots[i * 2 + 1]?.Id,
                    RoundNumber = 1,
                    MatchOrder = i,
                    Status = TeamMatchStatus.Pending
                };
                currentRoundTeamMatches.Add(tm);
                allTeamMatches.Add(tm);
            }

            for (int round = 2; round <= totalRounds; round++)
            {
                matchesInRound /= 2;
                var nextRoundTeamMatches = new List<TeamMatchEntity>();
                for (int i = 0; i < matchesInRound; i++)
                {
                    var tm = new TeamMatchEntity
                    {
                        Id = Guid.NewGuid(),
                        TournamentId = tournamentId,
                        TournamentStageId = stage.Id,
                        RoundNumber = round,
                        MatchOrder = i,
                        Status = TeamMatchStatus.Pending
                    };
                    currentRoundTeamMatches[i * 2].NextTeamMatchId = tm.Id;
                    currentRoundTeamMatches[i * 2 + 1].NextTeamMatchId = tm.Id;
                    nextRoundTeamMatches.Add(tm);
                    allTeamMatches.Add(tm);
                }
                currentRoundTeamMatches = nextRoundTeamMatches;
            }

            // Auto-advance byes for TeamMatches
            AutoAdvanceTeamByes(allTeamMatches);

            if (tournament.HasThirdPlaceMatch)
                BuildThirdPlaceTeamMatchIfApplicable(allTeamMatches, totalRounds, shuffled.Count, tournamentId, stage.Id);

            // Save all TeamMatchEntities
            foreach (var tm in allTeamMatches)
            {
                await this.AppUnitOfWork.TeamMatchRepository.AddEntity(tm, this.UserContextReader);
            }

            // Create sub-matches with a single batched member lookup (avoids per-match N+1)
            var membersByParticipant = await BuildMembersByParticipantMap(shuffled);
            var rand = new Random();

            foreach (var tm in allTeamMatches)
            {
                if (!tm.HomeTeamParticipantId.HasValue || !tm.AwayTeamParticipantId.HasValue)
                    continue;
                if (tm.Status == TeamMatchStatus.Completed)
                    continue;

                var subs = BuildSubMatchesForTeamMatch(tm, teamSize, null, membersByParticipant, rand);
                foreach (var sm in subs)
                    await this.AppUnitOfWork.MatchRepository.AddEntity(sm, this.UserContextReader);
            }

            await this.SaveAsync();
        }

        private static void AutoAdvanceTeamByes(List<TeamMatchEntity> allTeamMatches)
        {
            var matchesById = allTeamMatches
                .Where(m => m.Id.HasValue)
                .ToDictionary(m => m.Id!.Value, m => m);

            var firstRound = allTeamMatches
                .Where(m => (m.RoundNumber ?? 1) == 1)
                .OrderBy(m => m.MatchOrder)
                .ToList();

            foreach (var tm in firstRound)
            {
                bool hasHome = tm.HomeTeamParticipantId.HasValue;
                bool hasAway = tm.AwayTeamParticipantId.HasValue;

                if (hasHome == hasAway)
                {
                    if (!hasHome) tm.Status = TeamMatchStatus.Completed;
                    continue;
                }

                var winnerId = tm.HomeTeamParticipantId ?? tm.AwayTeamParticipantId;
                tm.WinnerTeamParticipantId = winnerId;
                tm.Status = TeamMatchStatus.Completed;

                if (winnerId.HasValue && tm.NextTeamMatchId.HasValue && matchesById.TryGetValue(tm.NextTeamMatchId.Value, out var nextTm))
                {
                    bool isHomeSlot = (tm.MatchOrder % 2) == 0;
                    if (isHomeSlot) nextTm.HomeTeamParticipantId = winnerId;
                    else nextTm.AwayTeamParticipantId = winnerId;
                }
            }
        }

        #endregion 2. Generators

        #region 3. Result Processing & Updates

        public async Task UpdateMatchResult(MatchResultDto request)
        {
            var match = await this.AppUnitOfWork.MatchRepository.GetWithStage(request.MatchId);

            if (match == null) throw new BusinessRuleException("Match not found");
            if (match.TournamentId != request.TournamentId) throw new BusinessRuleException("Match wrong tournament");
            if (match.RoundOpenAt.HasValue && match.RoundOpenAt.Value > DateTime.UtcNow)
                throw new BusinessRuleException("This round is not open yet.");

            // A solo match without both sides can't take a result — covers Swiss byes
            // (pre-completed free wins) and TBD elimination slots still awaiting feeders.
            // Team sub-matches always carry both participant ids, so they pass through.
            if (!match.TeamMatchId.HasValue && (!match.HomeParticipantId.HasValue || !match.AwayParticipantId.HasValue))
                throw new BusinessRuleException("This match has no opponent yet and cannot be reported.");

            MatchEntity? nextMatch = null;
            if (match.NextMatchId.HasValue)
            {
                nextMatch = match.NextMatch
                    ?? await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchId.Value);
            }

            // Semi-finals with a third-place play-off link their loser into this match.
            MatchEntity? loserBracketMatch = null;
            if (match.NextMatchLoserBracketId.HasValue)
            {
                loserBracketMatch = match.NextMatchLoserBracket
                    ?? await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchLoserBracketId.Value);
            }

            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var approvalCtx = await this.AppUnitOfWork.TournamentRepository.GetApprovalContext(match.TournamentId)
                ?? throw new BusinessRuleException("Tournament not found");
            bool isPrivileged = await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, currentUser);

            // When the tournament requires result approval and the caller is a participant
            // (not a tournament manager — platform admin, hub owner, or hub admin),
            // persist a proposal instead of completing the match.
            if (approvalCtx.RequireResultApproval && !isPrivileged)
            {
                if (!IsMatchParticipant(match, currentUser.UserId))
                    throw new BusinessRuleException("You are not a participant of this match.");

                // Once a result is confirmed in approval mode, only an admin / hub owner can change it.
                // Otherwise the approval gate could be bypassed via the edit path.
                if (match.Status == MatchStatus.Completed)
                    throw new BusinessRuleException("This result is final. Ask the hub owner or an admin to amend it.");

                await SaveProposal(match, request.HomeScore, request.AwayScore, currentUser);
                return;
            }

            // 1. REVERT LOGIC
            if (match.Status == MatchStatus.Completed)
            {
                // Same-winner in-place edit (solo elimination): only the score line changes while the
                // winner — and therefore everything that advanced downstream — stays identical, so there
                // is nothing to revert and no downstream lock to honour. This is the common "typed the
                // wrong score" fix; a cascade here would needlessly wipe already-played downstream results.
                if (!match.TeamMatchId.HasValue
                    && IsElimination(match.TournamentStage?.Type)
                    && WinnerSideUnchanged(match, request.HomeScore, request.AwayScore))
                {
                    await UpdateScoreInPlaceAsync(match, request.HomeScore, request.AwayScore);
                    return;
                }

                var lockReason = await GetDownstreamLockReasonAsync(match, nextMatch, loserBracketMatch, forEdit: true);
                if (lockReason != null)
                {
                    // Default (and every existing client): refuse, naming the downstream match to revert
                    // first. Opt-in cascade reverts that chain automatically (owner/admin only, solo
                    // brackets only) so the winner-changing edit can go through.
                    if (!request.Cascade)
                        throw new BusinessRuleException(lockReason);
                    if (!isPrivileged)
                        throw new BusinessRuleException("Only the hub owner or an admin can change a result once downstream matches have been played.");
                    if (match.TeamMatchId.HasValue)
                        throw new BusinessRuleException("Changing this result would undo already-played team matches. Revert the downstream team match first.");

                    await CascadeRevertDownstream(match);

                    // The chain below is reopened now; reload the target and its links from committed state.
                    match = await this.AppUnitOfWork.MatchRepository.GetWithStage(request.MatchId)
                        ?? throw new BusinessRuleException("Match not found");
                    (nextMatch, loserBracketMatch) = await LoadDownstreamRefsAsync(match);
                }

                if (match.TournamentStage?.Type == StageType.League || match.TournamentStage?.Type == StageType.GroupStage)
                {
                    // Solo group/league stats are resynced from scratch at the end of FinalizeMatchResult,
                    // so no explicit revert is needed here. Team matches still revert in place because
                    // their stats path hasn't been migrated to the resync model yet.
                    if (match.TeamMatchId.HasValue)
                        await RevertTeamLeagueMatchStats(match);
                }
                else if (IsElimination(match.TournamentStage?.Type))
                {
                    if (match.TeamMatchId.HasValue)
                        await RevertTeamMatchResult(match);
                    else
                        await RevertEliminationResult(match, nextMatch, loserBracketMatch);
                }
            }

            await FinalizeMatchResult(match, nextMatch, loserBracketMatch, request.HomeScore, request.AwayScore, request.TournamentId);
        }

        /// <summary>
        /// Deletes a completed match's result and reopens the match (status back to Scheduled,
        /// scores and winner cleared). Used when a result was entered by mistake / on the wrong
        /// match. Reuses the same downstream-lock guards and revert primitives as the edit path
        /// in <see cref="UpdateMatchResult"/>, so the bracket / standings stay consistent.
        /// Caller must be a match participant or a tournament manager (platform admin / hub owner /
        /// hub admin). When <paramref name="cascade"/> is set, an owner/admin can also delete a
        /// result whose downstream matches were already played: every already-played match below it
        /// (next round + loser-bracket drop, transitively) is reverted first, deepest-first.
        /// </summary>
        public async Task RevertMatchResult(Guid matchId, bool cascade = false)
        {
            var match = await this.AppUnitOfWork.MatchRepository.GetWithStage(matchId);
            if (match == null) throw new BusinessRuleException("Match not found");

            if (match.Status != MatchStatus.Completed)
                throw new BusinessRuleException("This match has no result to delete.");

            // Byes / one-sided completions (Swiss free wins, elimination walkovers) carry no
            // real reported result — nothing to delete.
            if (!match.TeamMatchId.HasValue && (!match.HomeParticipantId.HasValue || !match.AwayParticipantId.HasValue))
                throw new BusinessRuleException("This match has no result to delete.");

            MatchEntity? nextMatch = null;
            if (match.NextMatchId.HasValue)
            {
                nextMatch = match.NextMatch
                    ?? await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchId.Value);
            }

            MatchEntity? loserBracketMatch = null;
            if (match.NextMatchLoserBracketId.HasValue)
            {
                loserBracketMatch = match.NextMatchLoserBracket
                    ?? await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchLoserBracketId.Value);
            }

            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var approvalCtx = await this.AppUnitOfWork.TournamentRepository.GetApprovalContext(match.TournamentId)
                ?? throw new BusinessRuleException("Tournament not found");
            bool isPrivileged = await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, currentUser);

            // Trust boundary matches UpdateMatchResult: the controller [Authorize] plus the
            // mobile client (which only surfaces delete via the CanRevert flag — i.e. to match
            // participants and tournament managers) gate who can act, so the non-approval path
            // intentionally does not re-check match participation here. In approval mode a
            // confirmed result is locked to managers, mirroring how UpdateMatchResult refuses a
            // participant edit of a confirmed result.
            if (!isPrivileged && approvalCtx.RequireResultApproval)
                throw new BusinessRuleException("This result is final. Ask the hub owner or an admin to delete it.");

            var lockReason = await GetDownstreamLockReasonAsync(match, nextMatch, loserBracketMatch, forEdit: false);
            if (lockReason != null)
            {
                // Default (and every existing client): refuse, naming the downstream match to revert
                // first. Opt-in cascade reverts that whole chain automatically (owner/admin only, solo
                // brackets only) so this result can then be deleted.
                if (!cascade)
                    throw new BusinessRuleException(lockReason);
                if (!isPrivileged)
                    throw new BusinessRuleException("Only the hub owner or an admin can delete a result once downstream matches have been played.");
                if (match.TeamMatchId.HasValue)
                    throw new BusinessRuleException("Deleting this result would undo already-played team matches. Revert the downstream team match first.");

                await CascadeRevertDownstream(match);

                // The chain below is reopened now; reload the target and its links from committed state.
                match = await this.AppUnitOfWork.MatchRepository.GetWithStage(matchId)
                    ?? throw new BusinessRuleException("Match not found");
                (nextMatch, loserBracketMatch) = await LoadDownstreamRefsAsync(match);
            }

            await RevertMatchResultCore(match, nextMatch, loserBracketMatch);
        }

        // Core of a single-match revert: undo this match's own advancement / stats, clear its result,
        // reopen it, and invalidate caches. Split out of RevertMatchResult so the cascade path can
        // reuse the exact, tested behaviour on each downstream match without re-running the auth and
        // downstream-lock checks (the cascade walks deepest-first, so each step's downstream is already
        // reopened by the time it runs).
        private async Task RevertMatchResultCore(MatchEntity match, MatchEntity? nextMatch, MatchEntity? loserBracketMatch)
        {
            // 1. Revert downstream advancement / stats, reusing the same primitives as the edit
            //    path. Solo league/group/Swiss stats are derived from the Match table, so they are
            //    re-synced below once this match drops out of "Completed" (mirrors how
            //    FinalizeMatchResult re-derives them).
            bool reSyncSoloStats = false;
            if (match.TournamentStage?.Type == StageType.League
                || match.TournamentStage?.Type == StageType.GroupStage
                || match.TournamentStage?.Type == StageType.Swiss)
            {
                if (match.TeamMatchId.HasValue)
                    await RevertTeamLeagueMatchStats(match);
                else
                    reSyncSoloStats = true;
            }
            else if (IsElimination(match.TournamentStage?.Type))
            {
                if (match.TeamMatchId.HasValue)
                    await RevertTeamMatchResult(match);
                else
                    await RevertEliminationResult(match, nextMatch, loserBracketMatch);
            }

            // 2. Clear the result and reopen the match. Solo matches return to Scheduled — they
            //    keep their opponents and scheduled time and can be re-reported. Team sub-matches
            //    return to Pending instead, the only non-completed state the team flow uses, so the
            //    team-match detail screen shows them as "awaiting result" again (and re-aggregates
            //    the parent team match on the next report).
            match.Status = match.TeamMatchId.HasValue ? MatchStatus.Pending : MatchStatus.Scheduled;
            match.HomeUserScore = null;
            match.AwayUserScore = null;
            match.WinnerParticipantId = null;
            match.ProposedHomeScore = null;
            match.ProposedAwayScore = null;
            match.ProposedByUserId = null;
            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);

            if (reSyncSoloStats)
            {
                // Serialised per tournament, same as the apply path — the resync reads every other
                // completed match in scope, so it must not race a concurrent finalise.
                await this.AppUnitOfWork.TournamentRepository.AcquireAdvancementLock(match.TournamentId);
                try
                {
                    await ResyncSoloLeagueStatistics(match);

                    // League and Swiss publish a tournament winner once every match is played;
                    // deleting a result means the competition is no longer fully played, so roll
                    // the published winner / Completed status back. (A group stage only seeds the
                    // knockout — it never completes the tournament — so there is nothing to undo.)
                    if (match.TournamentStage?.Type == StageType.League || match.TournamentStage?.Type == StageType.Swiss)
                    {
                        var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(match.TournamentId);
                        if (tournament.Status == TournamentStatus.Completed)
                        {
                            tournament.Status = TournamentStatus.InProgress;
                            tournament.WinnerUserId = null;
                            tournament.WinnerTeamId = null;
                            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
                        }
                    }

                    await this.SaveAsync();
                }
                finally
                {
                    await this.AppUnitOfWork.TournamentRepository.ReleaseAdvancementLock(match.TournamentId);
                }
            }
            else
            {
                await this.SaveAsync();
            }

            await InvalidateMatchCachesAsync(match);
        }

        // Drop the caches a result change touches (mirrors FinalizeMatchResult): per-player stats for
        // everyone in the match plus the tournament's bracket / standings / PDF snapshots.
        private async Task InvalidateMatchCachesAsync(MatchEntity match)
        {
            var affectedUserIds = new HashSet<Guid>();
            if (match.HomeParticipant?.UserId != null) affectedUserIds.Add(match.HomeParticipant.UserId.Value);
            if (match.AwayParticipant?.UserId != null) affectedUserIds.Add(match.AwayParticipant.UserId.Value);
            if (match.HomeUserId.HasValue) affectedUserIds.Add(match.HomeUserId.Value);
            if (match.AwayUserId.HasValue) affectedUserIds.Add(match.AwayUserId.Value);

            if (match.HomeParticipant?.Team?.Members != null)
                foreach (var m in match.HomeParticipant.Team.Members.Where(m => m.UserId.HasValue))
                    affectedUserIds.Add(m.UserId!.Value);

            if (match.AwayParticipant?.Team?.Members != null)
                foreach (var m in match.AwayParticipant.Team.Members.Where(m => m.UserId.HasValue))
                    affectedUserIds.Add(m.UserId!.Value);

            foreach (var userId in affectedUserIds)
                await cacheService.RemoveAsync($"player_stats:{userId}");

            await cacheService.RemoveAsync($"bracket:{match.TournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{match.TournamentId}");
            await cacheService.RemoveAsync($"league_standings:{match.TournamentId}");
            await cacheService.RemoveAsync($"pdf:bracket:{match.TournamentId}");
        }

        // Returns the lock message when this completed match can't be reverted/edited in place because
        // something downstream has already progressed (next round, third-place play-off, or the
        // loser-bracket match its loser dropped into); null when it is safe. <paramref name="forEdit"/>
        // only varies the wording so the existing edit vs delete messages stay byte-identical.
        // True once the knockout bracket fed by a group stage has been drawn (its matches exist).
        // Group results feed that bracket through the standings, not a Next* link, so reverting or
        // editing one after the draw would desync the seeding — callers lock the result until reset.
        private async Task<bool> GroupKnockoutAlreadyDrawnAsync(Guid tournamentId)
        {
            var knockoutStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 2);
            return knockoutStage != null
                && (knockoutStage.Type == StageType.SingleEliminationBracket || knockoutStage.Type == StageType.DoubleEliminationWinnersBracket)
                && await this.AppUnitOfWork.MatchRepository.HasMatchesForStage(knockoutStage.Id!.Value);
        }

        private async Task<string?> GetDownstreamLockReasonAsync(MatchEntity match, MatchEntity? nextMatch, MatchEntity? loserBracketMatch, bool forEdit)
        {
            string verb = forEdit ? "To edit this, you must revert" : "You must revert";

            // Group results feed the knockout draw through the standings, not a Next* link, so the
            // checks below never catch them. Once the bracket is drawn, changing a group result would
            // desync the seeding from the standings — lock it until the bracket is reset.
            if (match.TournamentStage?.Type == StageType.GroupStage && await GroupKnockoutAlreadyDrawnAsync(match.TournamentId))
                return "The knockout bracket was already drawn from the group standings. Reset the bracket before changing a group result.";

            if (match.TeamMatchId.HasValue)
            {
                var parentTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(match.TeamMatchId.Value);

                if (parentTeamMatch.NextTeamMatchId.HasValue)
                {
                    var nextTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(parentTeamMatch.NextTeamMatchId.Value);
                    if (nextTeamMatch.Status != TeamMatchStatus.Pending)
                        return $"This match is locked because the next round has already progressed. {verb} the downstream match first.";
                }

                if (parentTeamMatch.NextTeamMatchLoserBracketId.HasValue)
                {
                    // Single-elim → third-place play-off; double-elim → the LB match the loser dropped into.
                    var loserBracketTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(parentTeamMatch.NextTeamMatchLoserBracketId.Value);
                    if (loserBracketTeamMatch.Status != TeamMatchStatus.Pending)
                        return $"This match is locked because the match its loser feeds into has already progressed. {verb} that match first.";
                }

                return null;
            }

            // Scheduled = both players agreed on a time but the match hasn't actually been played
            // (no Live/Completed/NoShow yet), so reverting is still safe. The downstream advancement
            // step clears the participant from the next match and resets it to Pending so the new
            // matchup can be re-scheduled.
            if (nextMatch != null && !IsDownstreamUnplayed(nextMatch.Status))
                return $"This match is locked because the next round has already progressed. {verb} the downstream match first.";

            if (loserBracketMatch != null && !IsDownstreamUnplayed(loserBracketMatch.Status))
            {
                // Single-elim → third-place play-off. DE → the Losers Bracket match that received this
                // WB match's loser. Same lock applies in both cases.
                bool isThirdPlace = loserBracketMatch.Stage == MatchStage.ThirdPlace;
                var label = isThirdPlace ? "third-place match" : "loser bracket match";
                return $"This match is locked because the {label} has already progressed. {verb} the downstream {label} first.";
            }

            return null;
        }

        // Downstream is "unplayed" (and therefore safe to reopen) when its status is Pending or
        // Scheduled — i.e. it hasn't reached Live / Completed / NoShow yet.
        private static bool IsDownstreamUnplayed(MatchStatus status)
            => status == MatchStatus.Pending || status == MatchStatus.Scheduled;

        // True when re-scoring a completed solo elimination match keeps the same winning side, so the
        // bracket below it is unaffected and the edit can be applied in place. A draw is never
        // "unchanged" (and isn't a valid elimination result anyway).
        private static bool WinnerSideUnchanged(MatchEntity match, int homeScore, int awayScore)
        {
            if (!match.WinnerParticipantId.HasValue) return false;
            if (homeScore == awayScore) return false;
            var newWinner = homeScore > awayScore ? match.HomeParticipantId : match.AwayParticipantId;
            return newWinner.HasValue && newWinner.Value == match.WinnerParticipantId.Value;
        }

        // Applies a same-winner score correction without touching the bracket: only the score line and
        // any stale proposal are updated; the winner (and everything that advanced from it) is left as-is.
        private async Task UpdateScoreInPlaceAsync(MatchEntity match, int homeScore, int awayScore)
        {
            match.HomeUserScore = homeScore;
            match.AwayUserScore = awayScore;
            match.ScheduledStartTime ??= DateTime.UtcNow;
            match.ProposedHomeScore = null;
            match.ProposedAwayScore = null;
            match.ProposedByUserId = null;
            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();
            await InvalidateMatchCachesAsync(match);
        }

        private static IEnumerable<Guid> DownstreamIds(MatchEntity m)
        {
            if (m.NextMatchId.HasValue) yield return m.NextMatchId.Value;
            if (m.NextMatchLoserBracketId.HasValue) yield return m.NextMatchLoserBracketId.Value;
        }

        // Every already-played match downstream of <paramref name="matchId"/>, ordered deepest-first
        // (a match appears only after everything below it), following both the winner edge (NextMatchId)
        // and the loser edge (NextMatchLoserBracketId) across stages. An unplayed match stops the walk —
        // nothing below it can be played. Solo brackets only; team links live on TeamMatchEntity. Read
        // from the no-tracking snapshot, so callers reload tracked entities before mutating them.
        private async Task<List<MatchEntity>> ComputeDownstreamCompletedChain(Guid tournamentId, Guid matchId)
        {
            var all = await this.AppUnitOfWork.MatchRepository.GetAllByTournamentId(tournamentId);
            var byId = all.Where(m => m.Id.HasValue).ToDictionary(m => m.Id!.Value, m => m);

            var collected = new Dictionary<Guid, MatchEntity>();
            var queue = new Queue<Guid>();
            if (byId.TryGetValue(matchId, out var start))
                foreach (var d in DownstreamIds(start)) queue.Enqueue(d);

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (collected.ContainsKey(id)) continue;
                if (!byId.TryGetValue(id, out var m)) continue;
                if (m.Status != MatchStatus.Completed) continue;
                collected[id] = m;
                foreach (var d in DownstreamIds(m)) queue.Enqueue(d);
            }

            // Topological order, leaves first (Kahn): repeatedly take the matches whose own downstream
            // is no longer in the remaining set. The bracket is a DAG, so this always drains.
            var ordered = new List<MatchEntity>();
            var remaining = new HashSet<Guid>(collected.Keys);
            while (remaining.Count > 0)
            {
                var leaves = remaining
                    .Where(id => DownstreamIds(collected[id]).All(d => !remaining.Contains(d)))
                    .ToList();
                if (leaves.Count == 0)
                {
                    ordered.AddRange(remaining.Select(id => collected[id])); // safety net; unreachable for a DAG
                    break;
                }
                foreach (var id in leaves) ordered.Add(collected[id]);
                foreach (var id in leaves) remaining.Remove(id);
            }

            return ordered;
        }

        // Reverts every already-played match downstream of the target (deepest-first) so the target can
        // then be deleted or re-finalised. Each step reuses the proven single-match revert core; because
        // we go deepest-first, each match's own downstream is already reopened when its turn comes, so no
        // downstream-lock is violated.
        private async Task CascadeRevertDownstream(MatchEntity match)
        {
            var chain = await ComputeDownstreamCompletedChain(match.TournamentId, match.Id!.Value);
            foreach (var snapshot in chain)
            {
                // Reload fresh (reads are no-tracking) and skip anything a prior step already reopened.
                var dm = await this.AppUnitOfWork.MatchRepository.GetWithStage(snapshot.Id!.Value);
                if (dm == null || dm.Status != MatchStatus.Completed) continue;
                var (dnext, dlb) = await LoadDownstreamRefsAsync(dm);
                await RevertMatchResultCore(dm, dnext, dlb);

                // RevertMatchResultCore attaches everything it saved (UpdateEntity sets state Modified),
                // and a downstream match can be another match's next/loser link, so drop the tracked
                // instances before the next reload to avoid an identity collision — same save-then-reload
                // discipline as the double-walkover settle pass. Leaves the tracker clean for the caller's
                // post-cascade reload of the target.
                this.AppUnitOfWork.MatchRepository.DetachAll();
            }
        }

        private async Task<(MatchEntity? nextMatch, MatchEntity? loserBracketMatch)> LoadDownstreamRefsAsync(MatchEntity match)
        {
            MatchEntity? nextMatch = null;
            if (match.NextMatchId.HasValue)
                nextMatch = match.NextMatch ?? await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchId.Value);

            MatchEntity? loserBracketMatch = null;
            if (match.NextMatchLoserBracketId.HasValue)
                loserBracketMatch = match.NextMatchLoserBracket ?? await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchLoserBracketId.Value);

            return (nextMatch, loserBracketMatch);
        }

        /// <summary>
        /// Read-only preview of what a cascade delete/edit on this match would reopen: every
        /// already-played downstream match, in the order it would be reverted (deepest-first). Lets the
        /// client list the collateral before the user confirms. Owner/admin only; returns an empty list
        /// when nothing downstream has been played (no cascade needed).
        /// </summary>
        public async Task<List<CascadeAffectedMatchDto>> GetCascadeRevertPreview(Guid matchId)
        {
            var match = await this.AppUnitOfWork.MatchRepository.GetWithStage(matchId);
            if (match == null) throw new BusinessRuleException("Match not found");

            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            if (!await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, currentUser))
                throw new BusinessRuleException("Only the hub owner or an admin can preview a cascade revert.");

            var chain = await ComputeDownstreamCompletedChain(match.TournamentId, matchId);

            return chain.Select(m => new CascadeAffectedMatchDto
            {
                MatchId = m.Id!.Value,
                Round = m.RoundNumber ?? 1,
                Stage = m.Stage,
                IsUpperBracket = m.IsUpperBracket,
                HomeScore = m.HomeUserScore ?? 0,
                AwayScore = m.AwayUserScore ?? 0,
            }).ToList();
        }

        /// <summary>
        /// Approves the pending proposal stored on the match: commits the proposed scores,
        /// clears proposal fields, advances the bracket. Caller must be the opposing participant
        /// or an admin / hub owner.
        /// </summary>
        public async Task ApproveProposedResult(Guid matchId)
        {
            var match = await this.AppUnitOfWork.MatchRepository.GetWithStage(matchId);
            if (match == null) throw new BusinessRuleException("Match not found");

            if (match.ProposedByUserId == null || match.ProposedHomeScore == null || match.ProposedAwayScore == null)
                throw new BusinessRuleException("No pending result to approve for this match.");

            if (match.Status == MatchStatus.Completed)
                throw new BusinessRuleException("This match is already completed.");

            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            bool isPrivileged = await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, currentUser);

            if (!isPrivileged)
            {
                if (!IsMatchParticipant(match, currentUser.UserId))
                    throw new BusinessRuleException("You are not a participant of this match.");

                // The proposer cannot also be the approver — the opponent (or a tournament manager) confirms.
                if (match.ProposedByUserId == currentUser.UserId)
                    throw new BusinessRuleException("Your opponent must approve the result you reported.");
            }

            MatchEntity? nextMatch = null;
            if (match.NextMatchId.HasValue)
                nextMatch = match.NextMatch ?? await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchId.Value);

            MatchEntity? loserBracketMatch = null;
            if (match.NextMatchLoserBracketId.HasValue)
                loserBracketMatch = match.NextMatchLoserBracket ?? await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchLoserBracketId.Value);

            int homeScore = match.ProposedHomeScore!.Value;
            int awayScore = match.ProposedAwayScore!.Value;
            var proposerId = match.ProposedByUserId!.Value;

            await FinalizeMatchResult(match, nextMatch, loserBracketMatch, homeScore, awayScore, match.TournamentId);
        }

        /// <summary>
        /// Rejects the pending proposal: clears proposal fields and notifies the proposer so they
        /// can submit a corrected result. Match stays in its pre-proposal state.
        /// </summary>
        public async Task RejectProposedResult(Guid matchId)
        {
            var match = await this.AppUnitOfWork.MatchRepository.ShallowGetById(matchId);
            if (match == null) throw new BusinessRuleException("Match not found");

            if (match.ProposedByUserId == null)
                throw new BusinessRuleException("No pending result to reject for this match.");

            if (match.Status == MatchStatus.Completed)
                throw new BusinessRuleException("This match is already completed.");

            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            bool isPrivileged = await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, currentUser);

            if (!isPrivileged)
            {
                var fullMatch = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
                if (fullMatch == null || !IsMatchParticipant(fullMatch, currentUser.UserId))
                    throw new BusinessRuleException("You are not a participant of this match.");

                if (match.ProposedByUserId == currentUser.UserId)
                    throw new BusinessRuleException("You can't reject your own proposal — submit a corrected result instead.");
            }

            match.ProposedHomeScore = null;
            match.ProposedAwayScore = null;
            match.ProposedByUserId = null;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();
            await cacheService.RemoveAsync($"bracket:{match.TournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{match.TournamentId}");
            await cacheService.RemoveAsync($"league_standings:{match.TournamentId}");

            // Proposal cleared → the opponent's "result to confirm" badge drops. Refresh both
            // sides (participants weren't loaded above, so fetch them just for the badge bump).
            var participantsMatch = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (participantsMatch != null) await BumpMatchBadgesAsync(participantsMatch);
        }

        /// <summary>
        /// Admin/owner action for an elimination match that was never played because BOTH sides
        /// failed to show: the match is closed with no winner (both eliminated) and the opponent
        /// coming from the sibling matchup advances unopposed — a walkover into the next round.
        /// Works whether or not the sibling has been decided yet: if it has, the present opponent is
        /// advanced now; if not, the walkover is applied automatically once that sibling completes
        /// (the settle hook on the advance path fires then). Caller must be able to manage the
        /// tournament. Solo elimination only — team matches are rejected.
        /// </summary>
        public async Task ApplyDoubleWalkover(Guid matchId)
        {
            var match = await this.AppUnitOfWork.MatchRepository.GetWithStage(matchId)
                ?? throw new BusinessRuleException("Match not found");

            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            if (!await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, currentUser))
                throw new BusinessRuleException("Only the hub owner or an admin can apply a double walkover.");

            if (match.TeamMatchId.HasValue)
                throw new BusinessRuleException("Double walkover isn't available for team matches.");

            if (!IsElimination(match.TournamentStage?.Type))
                throw new BusinessRuleException("Double walkover is only available in elimination brackets.");

            if (match.Status == MatchStatus.Completed)
                throw new BusinessRuleException("This match is already completed.");

            if (!match.HomeParticipantId.HasValue || !match.AwayParticipantId.HasValue)
                throw new BusinessRuleException("Both players must be set before a double walkover.");

            // Needs somewhere to advance the opponent into — a terminal match (final) has no next.
            if (!match.NextMatchId.HasValue && !match.NextMatchLoserBracketId.HasValue)
                throw new BusinessRuleException("This match has no next round, so a walkover can't advance anyone.");

            // Serialised per tournament, exactly like FinalizeMatchResult: the settle pass is
            // check-then-act over many rows and must not race a concurrent result report.
            await this.AppUnitOfWork.TournamentRepository.AcquireAdvancementLock(match.TournamentId);
            try
            {
                // Close the match with no winner. A Completed elimination match with no
                // WinnerParticipantId is the codebase's existing "dead feeder" signal (see
                // SourceProducesWinner) — both players are out, nothing advances from here.
                match.Status = MatchStatus.Completed;
                match.WinnerParticipantId = null;
                match.HomeUserScore = null;
                match.AwayUserScore = null;
                match.ProposedHomeScore = null;
                match.ProposedAwayScore = null;
                match.ProposedByUserId = null;
                match.ScheduledStartTime ??= DateTime.UtcNow;
                await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
                await this.SaveAsync();

                // Now that the void is committed, advance the surviving opponent(s) through every
                // match the void forces (winner and loser edges, cascading across rounds / stages).
                await SettleForcedWalkovers(match.TournamentId);
            }
            finally
            {
                await this.AppUnitOfWork.TournamentRepository.ReleaseAdvancementLock(match.TournamentId);
            }

            // Cache / badge invalidation — mirrors FinalizeMatchResult.
            var affectedUserIds = new HashSet<Guid>();
            if (match.HomeParticipant?.UserId != null) affectedUserIds.Add(match.HomeParticipant.UserId.Value);
            if (match.AwayParticipant?.UserId != null) affectedUserIds.Add(match.AwayParticipant.UserId.Value);

            foreach (var userId in affectedUserIds)
            {
                await cacheService.RemoveAsync($"player_stats:{userId}");
                await this.badgeService.PushAsync(userId);
            }

            await cacheService.RemoveAsync($"bracket:{match.TournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{match.TournamentId}");
            await cacheService.RemoveAsync($"league_standings:{match.TournamentId}");
            await cacheService.RemoveAsync($"pdf:bracket:{match.TournamentId}");
        }

        /// <summary>
        /// Settles every elimination match whose outcome is now forced by a "dead feeder" — a
        /// Completed-with-no-winner match (a double walkover, or a void left by an upstream one).
        /// A match is forced when its empty slot(s) have no live feeder left to fill them:
        /// <list type="bullet">
        /// <item>one participant present, no live feeder for the other slot → that participant wins by
        /// walkover and is advanced (via the shared terminal / Grand-Final logic);</item>
        /// <item>no participant, no live feeder → the match is itself void.</item>
        /// </list>
        /// Runs as a fixpoint over freshly-committed state: each settle is saved before the next scan,
        /// so the cascade (winner edges, loser-bracket drops, voids — across rounds and stages) reads a
        /// consistent bracket and never relies on the repository's no-tracking snapshots being in sync.
        /// Idempotent: a tournament with no dead feeders settles nothing. Caller holds the advancement lock.
        /// </summary>
        private async Task SettleForcedWalkovers(Guid tournamentId)
        {
            // Bounded by the match count; each productive pass marks one more match Completed. Each
            // pass reloads committed state, so the change tracker is cleared first (and after every
            // save) to drop the caller's / the previous pass's instances and avoid identity collisions.
            for (int guard = 0; guard < 1000; guard++)
            {
                this.AppUnitOfWork.MatchRepository.DetachAll();
                var all = await this.AppUnitOfWork.MatchRepository.GetAllByTournamentId(tournamentId);
                if (!await SettleOneForcedWalkover(all)) break;
            }
        }

        // Finds and settles a single forced match, saving the change. Returns false when none remain.
        private async Task<bool> SettleOneForcedWalkover(List<MatchEntity> all)
        {
            foreach (var n in all)
            {
                // Team sub-matches advance through the TeamMatch flow, not these Next* links.
                if (n.Status == MatchStatus.Completed || n.TeamMatchId.HasValue) continue;

                int filled = (n.HomeParticipantId.HasValue ? 1 : 0) + (n.AwayParticipantId.HasValue ? 1 : 0);
                if (filled == 2) continue;                  // a real match still to be played
                if (LiveFeederCount(n, all) > 0) continue;  // an opponent can still arrive — wait

                if (filled == 0)
                {
                    // No one is coming from either side → the match is void; mark it and let the next
                    // pass propagate the emptiness onward.
                    n.Status = MatchStatus.Completed;
                    n.WinnerParticipantId = null;
                    await this.AppUnitOfWork.MatchRepository.UpdateEntity(n, this.UserContextReader);
                    await this.SaveAsync();
                    return true;
                }

                // Exactly one participant and no opponent will ever arrive → walkover.
                Guid winnerId = n.HomeParticipantId ?? n.AwayParticipantId!.Value;
                Guid? winnerUserId = await ResolveParticipantUserId(winnerId);

                n.WinnerParticipantId = winnerId;
                n.Status = MatchStatus.Completed;
                await this.AppUnitOfWork.MatchRepository.UpdateEntity(n, this.UserContextReader);

                // Advance through the shared path (next-slot placement, Grand Final, tournament
                // completion). A walkover has no loser, so nothing drops to the loser bracket; the next
                // pass picks up any match that this one's missing loser now leaves forced.
                MatchEntity? nNext = n.NextMatchId.HasValue
                    ? all.FirstOrDefault(m => m.Id == n.NextMatchId.Value)
                    : null;
                await AdvanceWinnerToNextMatch(n, winnerId, winnerUserId, nNext);
                await this.SaveAsync();
                return true;
            }

            return false;
        }

        // Count of feeders that will still deliver a participant into <paramref name="n"/>: any
        // not-yet-Completed match pointing here on its winner edge (NextMatchId) or loser edge
        // (NextMatchLoserBracketId). Completed feeders have already deposited (or are dead ends and
        // never will), so they don't count.
        private static int LiveFeederCount(MatchEntity n, List<MatchEntity> all)
            => all.Count(f => f.Id != n.Id
                && f.Status != MatchStatus.Completed
                && (f.NextMatchId == n.Id || f.NextMatchLoserBracketId == n.Id));

        private async Task FinalizeMatchResult(
            MatchEntity match,
            MatchEntity? nextMatch,
            MatchEntity? loserBracketMatch,
            int homeScore,
            int awayScore,
            Guid tournamentId)
        {
            match.HomeUserScore = homeScore;
            match.AwayUserScore = awayScore;
            match.Status = MatchStatus.Completed;

            // Result entered straight through the bracket without ever scheduling — stamp the
            // entry time so the match doesn't show without a date. A real scheduled time is kept.
            match.ScheduledStartTime ??= DateTime.UtcNow;

            // Clear any proposal — the result is now official.
            match.ProposedHomeScore = null;
            match.ProposedAwayScore = null;
            match.ProposedByUserId = null;

            // 3. Determine Winner
            Guid? winnerParticipientId = null;
            Guid? winnerUserId = null;

            if (homeScore > awayScore)
            {
                winnerParticipientId = match.HomeParticipantId;
                winnerUserId = match.HomeParticipant!.UserId;
            }
            else if (awayScore > homeScore)
            {
                winnerParticipientId = match.AwayParticipantId;
                winnerUserId = match.AwayParticipant!.UserId;
            }

            match.WinnerParticipantId = winnerParticipientId;

            Guid? loserParticipantId = null;
            if (winnerParticipientId.HasValue)
            {
                loserParticipantId = winnerParticipientId.Value == match.HomeParticipantId
                    ? match.AwayParticipantId
                    : match.HomeParticipantId;
            }

            // 4. Update match entity
            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);

            // 5. Apply Rules & Advance — serialised per tournament. Round-completion checks and
            // stage advancement (next Swiss round, play-in → knockout, groups → knockout) are
            // check-then-act over many rows; without the lock, the last two results of a round
            // landing concurrently could each see "round complete, nothing generated yet" and
            // both generate. The result save happens inside the lock so the check below always
            // sees every previously-finalised match.
            await this.AppUnitOfWork.TournamentRepository.AcquireAdvancementLock(tournamentId);
            try
            {
                if (match.TournamentStage?.Type == StageType.League || match.TournamentStage?.Type == StageType.GroupStage
                    || match.TournamentStage?.Type == StageType.Swiss)
                {
                    if (match.TeamMatchId.HasValue)
                    {
                        // Team sub-match: defer participant stats to team-match completion in ProcessTeamMatchResult
                        await this.SaveAsync();
                        await ProcessTeamMatchResult(match);
                        await CheckAndUnlockNextRound(match.TournamentId, match.TournamentStageId!.Value, match.RoundNumber ?? 1);
                    }
                    else
                    {
                        // Single source of truth: derive participant stats from the Match table.
                        // Replaces the increment/revert pattern, which drifted under concurrent updates
                        // or any path that applied/reverted inconsistently. Idempotent by construction.
                        await ResyncSoloLeagueStatistics(match);
                        await this.SaveAsync();

                        if (match.TournamentStage?.Type == StageType.GroupStage)
                        {
                            await CheckAndAdvanceGroupStage(match.TournamentId, match.TournamentStageId!.Value);
                            await CheckAndUnlockNextRound(match.TournamentId, match.TournamentStageId!.Value, match.RoundNumber ?? 1);
                        }
                        if (match.TournamentStage?.Type == StageType.League)
                        {
                            await CheckAndCompleteLeague(match.TournamentId);
                            await CheckAndUnlockNextRound(match.TournamentId, match.TournamentStageId!.Value, match.RoundNumber ?? 1);
                        }
                        if (match.TournamentStage?.Type == StageType.Swiss)
                        {
                            // Swiss rounds don't pre-exist, so there is no CheckAndUnlockNextRound here —
                            // the next round is paired and opened by the advance itself.
                            await CheckAndAdvanceSwissStage(match);
                        }
                    }
                }
                else if (IsElimination(match.TournamentStage?.Type))
                {
                    if (match.TeamMatchId.HasValue)
                    {
                        // Persist the sub-match result before reading team match state
                        await this.SaveAsync();
                        await ProcessTeamMatchResult(match);
                    }
                    else
                    {
                        // Solo elimination
                        if (winnerParticipientId == null)
                            throw new Exception("Draws not allowed in elimination matches. Someone must win!");

                        await AdvanceWinnerToNextMatch(match, winnerParticipientId.Value, winnerUserId, nextMatch);

                        if (loserBracketMatch != null && loserParticipantId.HasValue)
                            await AdvanceLoserToThirdPlace(match, loserParticipantId.Value, loserBracketMatch);

                        await this.SaveAsync();

                        // Swiss play-in: once the round is fully played, draw the knockout bracket
                        // from the direct berths + play-in winners.
                        if (match.TournamentStage?.Type == StageType.PlayIn)
                            await CheckAndAdvancePlayInStage(match);

                        // If this result placed a participant opposite a slot whose feeder was earlier
                        // double-walkover'd, that participant now advances by walkover. No-op unless a
                        // dead feeder exists. Runs last, on committed state (it clears the change tracker).
                        await SettleForcedWalkovers(match.TournamentId);
                    }
                }
            }
            finally
            {
                await this.AppUnitOfWork.TournamentRepository.ReleaseAdvancementLock(tournamentId);
            }

            // Clear Cache
            var affectedUserIds = new HashSet<Guid>();
            if (match.HomeParticipant?.UserId != null) affectedUserIds.Add(match.HomeParticipant.UserId.Value);
            if (match.AwayParticipant?.UserId != null) affectedUserIds.Add(match.AwayParticipant.UserId.Value);

            if (match.HomeParticipant?.Team?.Members != null)
                foreach (var m in match.HomeParticipant.Team.Members.Where(m => m.UserId.HasValue))
                    affectedUserIds.Add(m.UserId!.Value);

            if (match.AwayParticipant?.Team?.Members != null)
                foreach (var m in match.AwayParticipant.Team.Members.Where(m => m.UserId.HasValue))
                    affectedUserIds.Add(m.UserId!.Value);

            foreach (var userId in affectedUserIds)
                await cacheService.RemoveAsync($"player_stats:{userId}");

            // Result is now official → any "result to confirm" badge on these participants
            // clears, and the schedule badge may change. Refresh both sides live.
            foreach (var userId in affectedUserIds)
                await this.badgeService.PushAsync(userId);

            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{tournamentId}");
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveAsync($"pdf:bracket:{tournamentId}");
        }

        private async Task SaveProposal(MatchEntity match, int homeScore, int awayScore, TokenUserInfo currentUser)
        {
            match.ProposedHomeScore = homeScore;
            match.ProposedAwayScore = awayScore;
            match.ProposedByUserId = currentUser.UserId;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();
            await cacheService.RemoveAsync($"bracket:{match.TournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{match.TournamentId}");
            await cacheService.RemoveAsync($"league_standings:{match.TournamentId}");

            // The opponent now has a result waiting for them — bump their badge and push.
            await NotifyResultProposedAsync(match, currentUser);
        }

        // Notifies the participant who did NOT propose the result that one is awaiting their
        // confirmation. Badge bump first (works even without a push token), then a fire-and-forget
        // push with already-resolved data so the background send never touches this DbContext.
        private async Task NotifyResultProposedAsync(MatchEntity match, TokenUserInfo proposer)
        {
            var homeUserId = match.HomeUserId ?? match.HomeParticipant?.UserId;
            var awayUserId = match.AwayUserId ?? match.AwayParticipant?.UserId;

            Guid? opponentUserId =
                homeUserId == proposer.UserId ? awayUserId :
                awayUserId == proposer.UserId ? homeUserId : null;

            if (opponentUserId == null) return;

            await this.badgeService.PushAsync(opponentUserId.Value);

            var opponent = await this.AppUnitOfWork.UserRepository.GetById(opponentUserId.Value);
            if (string.IsNullOrEmpty(opponent?.PushToken)) return;

            var token = opponent.PushToken!;
            var matchId = match.Id!.Value;
            var proposerName = proposer.Username;
            _ = Task.Run(async () =>
            {
                try
                {
                    await notificationService.SendToOneAsync(
                        token,
                        "Result to confirm",
                        $"{proposerName} reported a result. Tap to confirm or dispute.",
                        new { matchId = matchId.ToString(), type = "resultProposed" });
                }
                catch { /* fire-and-forget */ }
            });
        }

        // Refreshes both sides' badges after a proposal is confirmed or rejected (the opponent's
        // "results to confirm" count drops). Team sub-matches carry the player ids on the columns.
        private async Task BumpMatchBadgesAsync(MatchEntity match)
        {
            var homeUserId = match.HomeUserId ?? match.HomeParticipant?.UserId;
            var awayUserId = match.AwayUserId ?? match.AwayParticipant?.UserId;
            if (homeUserId.HasValue) await this.badgeService.PushAsync(homeUserId.Value);
            if (awayUserId.HasValue) await this.badgeService.PushAsync(awayUserId.Value);
        }

        // Fire-and-forget "you won" push to the champion (solo) or every member of the winning
        // team. Tokens are resolved here in the request scope and handed to the background send.
        private async Task NotifyTournamentWinnerAsync(TournamentEntity tournament)
        {
            try
            {
                List<Guid> winnerUserIds;

                if (tournament.WinnerUserId.HasValue && tournament.WinnerUserId.Value != Guid.Empty)
                {
                    winnerUserIds = new List<Guid> { tournament.WinnerUserId.Value };
                }
                else if (tournament.WinnerTeamId.HasValue)
                {
                    var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(tournament.WinnerTeamId.Value);
                    winnerUserIds = team?.Members
                        .Where(m => m.UserId.HasValue)
                        .Select(m => m.UserId!.Value)
                        .ToList() ?? new List<Guid>();
                }
                else
                {
                    return;
                }

                if (winnerUserIds.Count == 0) return;

                var pushTokens = await this.AppUnitOfWork.UserRepository.GetPushTokensByUserIds(winnerUserIds);
                if (pushTokens.Count == 0) return;

                var tournamentId = tournament.Id!.Value;
                var title = tournament.Name;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await notificationService.SendToManyAsync(
                            pushTokens,
                            title,
                            "Congratulations — you won the tournament! 🏆",
                            new { tournamentId, type = "tournamentWon" });
                    }
                    catch { /* fire-and-forget */ }
                });
            }
            catch { /* never let a win-notification failure break tournament completion */ }
        }

        private static bool IsMatchParticipant(MatchEntity match, Guid userId)
        {
            // Team sub-matches carry the player ids on the match itself; solo matches use the participants.
            if (match.TeamMatchId.HasValue)
                return match.HomeUserId == userId || match.AwayUserId == userId;

            if (match.HomeParticipant?.UserId == userId || match.AwayParticipant?.UserId == userId)
                return true;

            // Team-tournament finals/group matches with a captain-led team can have the team members
            // registered against the participant via the Team relationship.
            if (match.HomeParticipant?.Team?.Members?.Any(m => m.UserId == userId) == true) return true;
            if (match.AwayParticipant?.Team?.Members?.Any(m => m.UserId == userId) == true) return true;

            return false;
        }

        public async Task<bool> GetCanRevert(Guid matchId)
        {
            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(matchId);

            if (match.Status != MatchStatus.Completed)
                return false;

            // Byes (Swiss) and other one-sided completions have no opponent — nothing to revert.
            if (!match.TeamMatchId.HasValue && (!match.HomeParticipantId.HasValue || !match.AwayParticipantId.HasValue))
                return false;

            // Group results feed the knockout draw via standings; once the bracket is drawn a revert
            // would desync the seeding. Mirrors the server-side lock in RevertMatchResult.
            if (match.Stage == MatchStage.GroupStage && await GroupKnockoutAlreadyDrawnAsync(match.TournamentId))
                return false;

            bool isParticipant = match.TeamMatchId.HasValue
                ? match.HomeUserId == currentUser.UserId || match.AwayUserId == currentUser.UserId
                : match.HomeParticipant?.UserId == currentUser.UserId || match.AwayParticipant?.UserId == currentUser.UserId;

            if (!isParticipant)
                return false;

            // In approval-required tournaments, confirmed results are locked to participants.
            // Only admin / hub owner can revert (the structure endpoint's CanRevert is the
            // authoritative source for the UI; this is the matching guard for direct callers).
            var approvalCtx = await this.AppUnitOfWork.TournamentRepository.GetApprovalContext(match.TournamentId);
            if (approvalCtx?.RequireResultApproval == true)
                return false;

            if (match.TeamMatchId.HasValue)
            {
                // Team sub-matches don't carry the Next* links; the downstream links live on the parent
                // team match (next round + third-place play-off). Mirror the lock from UpdateMatchResult.
                var parentTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(match.TeamMatchId.Value);

                if (parentTeamMatch.NextTeamMatchId.HasValue)
                {
                    var nextTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(parentTeamMatch.NextTeamMatchId.Value);
                    if (nextTeamMatch.Status != TeamMatchStatus.Pending)
                        return false;
                }

                if (parentTeamMatch.NextTeamMatchLoserBracketId.HasValue)
                {
                    var thirdPlaceTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(parentTeamMatch.NextTeamMatchLoserBracketId.Value);
                    if (thirdPlaceTeamMatch.Status != TeamMatchStatus.Pending)
                        return false;
                }

                return true;
            }

            if (!match.NextMatchId.HasValue)
                return true;

            var nextMatch = await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchId.Value);

            return nextMatch.Status == MatchStatus.Pending &&
                   nextMatch.HomeParticipantId == null &&
                   nextMatch.AwayParticipantId == null &&
                   nextMatch.HomeUserScore == null &&
                   nextMatch.AwayUserScore == null;
        }

        #endregion 3. Result Processing & Updates

        #region 4. Data Access (Standings)

        public async Task<List<LeagueStandingDto>> GetLeagueStandings(Guid tournamentId)
        {
            // Cache key intentionally mirrors the bracket: any write path that invalidates
            // `bracket:{tournamentId}` also invalidates `league_standings:{tournamentId}`.
            string cacheKey = $"league_standings:{tournamentId}";
            var cached = await cacheService.GetAsync<List<LeagueStandingDto>>(cacheKey);
            if (cached != null) return cached;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);
            if (tournament == null) throw new Exception("Tournament not found");

            var standings = tournament.TournamentParticipants?
                .Select(p => new LeagueStandingDto
                {
                    ParticipantId = p.Id!.Value,
                    // Team participants have no UserId — see BuildGroupStandings.
                    UserId = p.UserId ?? Guid.Empty,
                    Points = p.Points,
                    Wins = p.Wins,
                    Draws = p.Draws,
                    Losses = p.Losses,
                    GoalsFor = p.GoalsFor,
                    GoalsAgainst = p.GoalsAgainst,
                    GoalDifference = p.GoalsFor - p.GoalsAgainst,
                    MatchesPlayed = p.Wins + p.Draws + p.Losses
                })
                .OrderByDescending(s => s.Points)
                .ThenByDescending(s => s.GoalDifference)
                .ThenByDescending(s => s.GoalsFor)
                .ToList();

            for (int i = 0; i < standings!.Count; i++) standings[i].Position = i + 1;

            await cacheService.SetAsync(cacheKey, standings, TimeSpan.FromMinutes(5));

            return standings;
        }

        #endregion 4. Data Access (Standings)

        #region 5. Private Helpers (Core Logic)

        // Single source of truth for solo group / league stats: derives Wins/Draws/Losses/GF/GA/Points
        // from the Match table. Idempotent — running it any number of times converges on the same
        // result — so it is immune to drift from concurrent UpdateMatchResult / ApproveProposedResult
        // calls, double-applied increments, or any other inconsistent revert/apply path.
        //
        // Cost per call (scoped to a single group, or to the whole league):
        //   1 DB read — completed matches in scope, projected to a tiny no-tracking row (excludes
        //               the current match: we apply its in-memory state instead so we don't need
        //               an intermediate SaveAsync to make the new score visible to the query).
        //   1 DB read — participants in scope (tracked, since we update them).
        //   1 DB write — batched in the caller's SaveAsync alongside the match update.
        private async Task ResyncSoloLeagueStatistics(MatchEntity match)
        {
            if (!match.TournamentStageId.HasValue) return;

            var otherMatchStats = await this.AppUnitOfWork.MatchRepository.GetCompletedSoloMatchStatsForGroup(
                match.TournamentStageId.Value,
                match.TournamentGroupId,
                excludeMatchId: match.Id);

            var participants = match.TournamentGroupId.HasValue
                ? await this.AppUnitOfWork.TournamentParticipantRepository.GetByGroupId(match.TournamentGroupId.Value)
                : await this.AppUnitOfWork.TournamentParticipantRepository.GetForLeagueResync(match.TournamentId);

            if (participants.Count == 0) return;

            var byId = participants.Where(p => p.Id.HasValue).ToDictionary(p => p.Id!.Value, p => p);

            foreach (var p in participants)
            {
                p.Points = 0; p.Wins = 0; p.Draws = 0; p.Losses = 0;
                p.GoalsFor = 0; p.GoalsAgainst = 0;
            }

            foreach (var m in otherMatchStats)
                ApplyMatchStats(byId, m.HomeParticipantId, m.AwayParticipantId, m.HomeScore, m.AwayScore, m.WinnerParticipantId);

            // Fold in the current match using its in-memory state — fresher than the DB.
            if (match.Status == MatchStatus.Completed
                && match.TeamMatchId == null
                && match.HomeParticipantId.HasValue
                && match.AwayParticipantId.HasValue)
            {
                ApplyMatchStats(
                    byId,
                    match.HomeParticipantId.Value,
                    match.AwayParticipantId.Value,
                    match.HomeUserScore ?? 0,
                    match.AwayUserScore ?? 0,
                    match.WinnerParticipantId);
            }

            foreach (var p in participants)
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(p, this.UserContextReader);
        }

        private static void ApplyMatchStats(
            Dictionary<Guid, TournamentParticipantEntity> byId,
            Guid homeId,
            Guid? awayId,
            int homeScore,
            int awayScore,
            Guid? winnerId)
        {
            if (!byId.TryGetValue(homeId, out var home)) return;

            // Swiss bye: no opponent — the sitting player banks a free win, no goals involved.
            // Mirror of the eager bye credit in BuildSwissRoundMatches — keep the two in sync.
            if (!awayId.HasValue)
            {
                if (winnerId == home.Id) { home.Wins++; home.Points += 3; }
                return;
            }

            if (!byId.TryGetValue(awayId.Value, out var away)) return;

            home.GoalsFor += homeScore; home.GoalsAgainst += awayScore;
            away.GoalsFor += awayScore; away.GoalsAgainst += homeScore;

            if (winnerId == home.Id) { home.Wins++; home.Points += 3; away.Losses++; }
            else if (winnerId == away.Id) { away.Wins++; away.Points += 3; home.Losses++; }
            else { home.Draws++; home.Points += 1; away.Draws++; away.Points += 1; }
        }

        private async Task RevertTeamLeagueMatchStats(MatchEntity subMatch)
        {
            var teamMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(subMatch.TeamMatchId!.Value);
            if (teamMatch == null) return;
            if (teamMatch.Status != TeamMatchStatus.Completed) return;

            int homeTotal = 0, awayTotal = 0;
            foreach (var sm in teamMatch.SubMatches)
            {
                homeTotal += sm.HomeUserScore ?? 0;
                awayTotal += sm.AwayUserScore ?? 0;
            }

            var winnerId = teamMatch.WinnerTeamParticipantId;

            var homePart = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(teamMatch.HomeTeamParticipantId!.Value);
            var awayPart = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(teamMatch.AwayTeamParticipantId!.Value);

            homePart.GoalsFor -= homeTotal; homePart.GoalsAgainst -= awayTotal;
            awayPart.GoalsFor -= awayTotal; awayPart.GoalsAgainst -= homeTotal;

            if (winnerId == teamMatch.HomeTeamParticipantId)
            { homePart.Wins--; homePart.Points -= 3; awayPart.Losses--; }
            else if (winnerId == teamMatch.AwayTeamParticipantId)
            { awayPart.Wins--; awayPart.Points -= 3; homePart.Losses--; }
            else
            { homePart.Draws--; homePart.Points -= 1; awayPart.Draws--; awayPart.Points -= 1; }

            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(homePart, this.UserContextReader);
            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(awayPart, this.UserContextReader);

            teamMatch.Status = TeamMatchStatus.Pending;
            teamMatch.WinnerTeamParticipantId = null;
            await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(teamMatch, this.UserContextReader);

            bool revertedTournament = false;
            if (subMatch.TournamentStage?.Type == StageType.League)
            {
                var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(teamMatch.TournamentId);
                if (tournament.Status == TournamentStatus.Completed)
                {
                    tournament.Status = TournamentStatus.InProgress;
                    tournament.WinnerUserId = null;
                    tournament.WinnerTeamId = null;
                    await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
                    revertedTournament = true;
                }
            }

            await this.SaveAsync();

            // Detach AFTER SaveAsync. Detaching a Modified entry BEFORE save drops the change
            // (the previous tournament DetachEntity above silently lost the InProgress revert),
            // and leaving teamMatch tracked after save crashes the downstream
            // ProcessTeamMatchResult -> ProcessTeamMatchResultInner reload+UpdateEntity at the
            // "another instance with the same key is already tracked" check.
            await this.AppUnitOfWork.TournamentParticipantRepository.DetachEntity(homePart);
            await this.AppUnitOfWork.TournamentParticipantRepository.DetachEntity(awayPart);
            await this.AppUnitOfWork.TeamMatchRepository.DetachEntity(teamMatch);
            if (revertedTournament)
                await this.AppUnitOfWork.TournamentRepository.DetachById(teamMatch.TournamentId);
        }

        private async Task RevertEliminationResult(MatchEntity match, MatchEntity? nextMatch, MatchEntity? loserBracketMatch)
        {
            // DE Grand Final with a reset child: reverting the GF removes the reset final entirely
            // (it only exists because the LB champion won this match). The caller's downstream lock
            // already refuses this when the reset has progressed, so here the reset is still Pending.
            if (match.Stage == MatchStage.GrandFinal && nextMatch != null && nextMatch.Stage == MatchStage.GrandFinalReset)
            {
                await this.AppUnitOfWork.MatchRepository.HardDeleteEntity(nextMatch);
                match.NextMatchId = null;
                match.NextMatchHomeAwaySlot = null;
                await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);

                // The reset being Pending means the tournament is still in progress, so there is
                // normally nothing to roll back — but cover the edge where it was completed anyway.
                var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(match.TournamentId);
                if (tournament.Status == TournamentStatus.Completed)
                {
                    tournament.Status = TournamentStatus.InProgress;
                    tournament.WinnerUserId = null;
                    await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
                    await this.SaveAsync();
                    await this.AppUnitOfWork.TournamentRepository.DetachEntity(tournament);
                }
                return;
            }

            if (nextMatch == null)
            {
                // Reverting either terminal match (the final or the third-place play-off) means the
                // tournament is no longer fully played, so roll back a previously-published winner/status.
                if (match.WinnerParticipantId.HasValue)
                {
                    var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(match.TournamentId);
                    if (tournament.Status == TournamentStatus.Completed)
                    {
                        tournament.Status = TournamentStatus.InProgress;
                        tournament.WinnerUserId = null;
                        await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
                        // Save+detach NOW. The previous detach-without-save dropped the Modified
                        // state before the caller's SaveAsync ran, silently losing the revert.
                        // The detach is still required so AdvanceWinnerToNextMatch's terminal branch
                        // can reload the tournament without a tracking-key conflict.
                        await this.SaveAsync();
                        await this.AppUnitOfWork.TournamentRepository.DetachEntity(tournament);
                    }
                }
                return;
            }

            if (match.WinnerParticipantId.HasValue)
            {
                bool changed = false;
                if (nextMatch.HomeParticipantId == match.WinnerParticipantId)
                {
                    nextMatch.HomeParticipantId = null;
                    changed = true;
                }
                else if (nextMatch.AwayParticipantId == match.WinnerParticipantId)
                {
                    nextMatch.AwayParticipantId = null;
                    changed = true;
                }

                if (changed)
                {
                    if (nextMatch.Status == MatchStatus.Completed)
                    {
                        nextMatch.Status = MatchStatus.Pending;
                        nextMatch.HomeUserScore = 0;
                        nextMatch.AwayUserScore = 0;
                        nextMatch.WinnerParticipantId = null;
                    }
                    else if (nextMatch.Status == MatchStatus.Scheduled)
                    {
                        // Both players had agreed on a time; pulling one of them out invalidates that
                        // schedule, so the slot returns to Pending and any agreed-on time is dropped.
                        // Players can re-agree once the upstream re-finalises and the slot is filled.
                        nextMatch.Status = MatchStatus.Pending;
                        nextMatch.ScheduledStartTime = null;
                    }
                    await this.AppUnitOfWork.MatchRepository.UpdateEntity(nextMatch, this.UserContextReader);
                }
            }

            // Reverting a semi-final must also pull its loser back out of the third-place play-off.
            if (loserBracketMatch != null && match.WinnerParticipantId.HasValue)
            {
                Guid? loserId = match.WinnerParticipantId.Value == match.HomeParticipantId
                    ? match.AwayParticipantId
                    : match.HomeParticipantId;

                if (loserId.HasValue)
                {
                    bool loserChanged = false;
                    if (loserBracketMatch.HomeParticipantId == loserId)
                    {
                        loserBracketMatch.HomeParticipantId = null;
                        loserChanged = true;
                    }
                    else if (loserBracketMatch.AwayParticipantId == loserId)
                    {
                        loserBracketMatch.AwayParticipantId = null;
                        loserChanged = true;
                    }

                    if (loserChanged)
                    {
                        if (loserBracketMatch.Status == MatchStatus.Completed)
                        {
                            loserBracketMatch.Status = MatchStatus.Pending;
                            loserBracketMatch.HomeUserScore = 0;
                            loserBracketMatch.AwayUserScore = 0;
                            loserBracketMatch.WinnerParticipantId = null;
                        }
                        else if (loserBracketMatch.Status == MatchStatus.Scheduled)
                        {
                            // Same logic as the winner side above: agreed time is no longer valid once
                            // a participant is pulled out, so reset to Pending.
                            loserBracketMatch.Status = MatchStatus.Pending;
                            loserBracketMatch.ScheduledStartTime = null;
                        }
                        await this.AppUnitOfWork.MatchRepository.UpdateEntity(loserBracketMatch, this.UserContextReader);
                    }
                }
            }
        }

        private async Task RevertTeamMatchResult(MatchEntity subMatch)
        {
            var teamMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(subMatch.TeamMatchId!.Value);
            if (teamMatch == null) return;

            if (teamMatch.Status != TeamMatchStatus.Completed && teamMatch.Status != TeamMatchStatus.TieBreakRequired)
                return;

            var oldWinner = teamMatch.WinnerTeamParticipantId;

            teamMatch.WinnerTeamParticipantId = null;
            teamMatch.Status = TeamMatchStatus.Pending;
            await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(teamMatch, this.UserContextReader);

            // DE Grand Final with a reset child: reverting the GF deletes the reset final entirely
            // (it exists only because the LB champion won). The caller's downstream lock already
            // refuses this once the reset has progressed, so here the reset is still Pending.
            if (teamMatch.IsGrandFinal && teamMatch.NextTeamMatchId.HasValue)
            {
                var resetMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(teamMatch.NextTeamMatchId.Value);
                if (resetMatch != null && resetMatch.IsGrandFinalReset)
                {
                    foreach (var sm in resetMatch.SubMatches)
                        await this.AppUnitOfWork.MatchRepository.HardDeleteEntity(sm);
                    await this.AppUnitOfWork.TeamMatchRepository.HardDeleteEntity(resetMatch);

                    teamMatch.NextTeamMatchId = null;
                    teamMatch.NextTeamMatchHomeAwaySlot = null;
                    await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(teamMatch, this.UserContextReader);
                }
            }
            else if (teamMatch.NextTeamMatchId.HasValue && oldWinner.HasValue)
            {
                var nextTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(teamMatch.NextTeamMatchId.Value);
                if (nextTeamMatch != null)
                {
                    bool isHomeSlot = teamMatch.NextTeamMatchHomeAwaySlot.HasValue
                        ? teamMatch.NextTeamMatchHomeAwaySlot.Value == 0
                        : (teamMatch.MatchOrder % 2) == 0;
                    if (isHomeSlot)
                        nextTeamMatch.HomeTeamParticipantId = null;
                    else
                        nextTeamMatch.AwayTeamParticipantId = null;

                    foreach (var sm in nextTeamMatch.SubMatches)
                    {
                        await this.AppUnitOfWork.MatchRepository.HardDeleteEntity(sm);
                    }

                    if (nextTeamMatch.Status == TeamMatchStatus.Completed)
                    {
                        nextTeamMatch.WinnerTeamParticipantId = null;
                        nextTeamMatch.Status = TeamMatchStatus.Pending;
                    }

                    await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(nextTeamMatch, this.UserContextReader);
                }
            }

            // Reverting a match must also pull its loser back out of the drop-in target —
            // the third-place play-off (single-elim) or the Losers Bracket match (double-elim).
            if (teamMatch.NextTeamMatchLoserBracketId.HasValue && oldWinner.HasValue)
            {
                var loserId = oldWinner.Value == teamMatch.HomeTeamParticipantId
                    ? teamMatch.AwayTeamParticipantId
                    : teamMatch.HomeTeamParticipantId;

                var loserBracketMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(teamMatch.NextTeamMatchLoserBracketId.Value);
                if (loserBracketMatch != null && loserId.HasValue)
                {
                    bool loserIsHomeSlot = teamMatch.NextTeamMatchLoserBracketHomeAwaySlot.HasValue
                        ? teamMatch.NextTeamMatchLoserBracketHomeAwaySlot.Value == 0
                        : (teamMatch.MatchOrder % 2) == 0;
                    if (loserIsHomeSlot)
                        loserBracketMatch.HomeTeamParticipantId = null;
                    else
                        loserBracketMatch.AwayTeamParticipantId = null;

                    foreach (var sm in loserBracketMatch.SubMatches)
                        await this.AppUnitOfWork.MatchRepository.HardDeleteEntity(sm);

                    if (loserBracketMatch.Status == TeamMatchStatus.Completed)
                    {
                        loserBracketMatch.WinnerTeamParticipantId = null;
                        loserBracketMatch.Status = TeamMatchStatus.Pending;
                    }

                    await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(loserBracketMatch, this.UserContextReader);
                }
            }

            // Reverting any completed elimination result means the tournament is no longer fully played,
            // so roll back a previously-published winner/Completed status (covers final, third-place and
            // semi-final cascades alike).
            bool revertedTournament = false;
            if (oldWinner.HasValue)
            {
                var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(teamMatch.TournamentId);
                if (tournament.Status == TournamentStatus.Completed)
                {
                    tournament.Status = TournamentStatus.InProgress;
                    tournament.WinnerUserId = null;
                    tournament.WinnerTeamId = null;
                    await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
                    revertedTournament = true;
                }
            }

            await this.SaveAsync();

            // Detach AFTER SaveAsync. The previous in-place DetachEntity calls (teamMatch above,
            // tournament here) ran BEFORE this save and dropped their Modified state — so
            // teamMatch.Status=Pending and tournament.Status=InProgress were silently never
            // written. Leaving nextTeamMatch / thirdPlaceMatch tracked through the function
            // return ALSO crashes the downstream ProcessTeamMatchResultInner reload+UpdateEntity
            // at "another instance with the same key is already tracked".
            await this.AppUnitOfWork.TeamMatchRepository.DetachEntity(teamMatch);
            if (teamMatch.NextTeamMatchId.HasValue && oldWinner.HasValue)
                await this.AppUnitOfWork.TeamMatchRepository.DetachById(teamMatch.NextTeamMatchId.Value);
            if (teamMatch.NextTeamMatchLoserBracketId.HasValue && oldWinner.HasValue)
                await this.AppUnitOfWork.TeamMatchRepository.DetachById(teamMatch.NextTeamMatchLoserBracketId.Value);
            if (revertedTournament)
                await this.AppUnitOfWork.TournamentRepository.DetachById(teamMatch.TournamentId);
        }

        private async Task AdvanceWinnerToNextMatch(MatchEntity match, Guid winnerId, Guid? winnerUserId, MatchEntity? nextMatch)
        {
            // Grand Final handling comes first: the GF never feeds a normal next match, and a stale
            // nextMatch reference can survive a revert that deleted the reset final — ignore it here.
            if (match.Stage == MatchStage.GrandFinal)
            {
                // Home = WB champion (entered undefeated); away = LB champion (one loss). If the WB
                // champion wins, the title is decided. If the LB champion wins, both now hold one
                // loss → a single reset Grand Final is created and played to decide the title.
                bool lbChampionWon = match.WinnerParticipantId.HasValue
                    && match.WinnerParticipantId == match.AwayParticipantId;

                if (lbChampionWon)
                    await CreateGrandFinalReset(match);
                else
                    await CompleteSoloTournament(match.TournamentId, winnerUserId);
            }
            else if (match.Stage == MatchStage.GrandFinalReset)
            {
                // Reset final: its winner is the champion.
                await CompleteSoloTournament(match.TournamentId, winnerUserId);
            }
            else if (nextMatch != null)
            {
                // DE pre-computes the destination slot to handle LB drop-ins (WB-loser → away,
                // LB-winner → home) where the legacy MatchOrder%2 pairing doesn't hold. Single
                // elimination leaves the override null and falls through to MatchOrder%2.
                bool isHomeSlot = match.NextMatchHomeAwaySlot.HasValue
                    ? match.NextMatchHomeAwaySlot.Value == 0
                    : (match.MatchOrder % 2) == 0;

                if (isHomeSlot)
                    nextMatch.HomeParticipantId = winnerId;
                else
                    nextMatch.AwayParticipantId = winnerId;

                await this.AppUnitOfWork.MatchRepository.UpdateEntity(nextMatch, this.UserContextReader);
            }
            else
            {
                // Single-elim terminal: the championship final or the third-place play-off. With a
                // third-place play-off the tournament is finished only once BOTH are played, and the
                // champion is always the winner of the final (never the third-place match).
                var stageMatches = await this.AppUnitOfWork.MatchRepository.GetByStageId(match.TournamentStageId!.Value);
                var finalMatch = stageMatches.FirstOrDefault(m => m.Stage == MatchStage.Final);
                var thirdPlaceMatch = stageMatches.FirstOrDefault(m => m.Stage == MatchStage.ThirdPlace);

                // The in-flight match isn't saved as Completed yet, so treat it as done via the Id shortcut.
                bool finalDone = finalMatch != null && (finalMatch.Id == match.Id || finalMatch.Status == MatchStatus.Completed);
                bool thirdPlaceDone = thirdPlaceMatch == null || thirdPlaceMatch.Id == match.Id || thirdPlaceMatch.Status == MatchStatus.Completed;

                if (finalDone && thirdPlaceDone)
                {
                    var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(match.TournamentId);
                    bool wasCompleted = tournament.Status == TournamentStatus.Completed;

                    tournament.WinnerUserId = finalMatch!.Id == match.Id
                        ? winnerUserId
                        : await ResolveParticipantUserId(finalMatch.WinnerParticipantId);
                    tournament.Status = TournamentStatus.Completed;
                    await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);

                    if (!wasCompleted)
                    {
                        await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
                        await NotifyTournamentWinnerAsync(tournament);
                    }
                }
            }
        }

        private async Task CompleteSoloTournament(Guid tournamentId, Guid? winnerUserId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);
            bool wasCompleted = tournament.Status == TournamentStatus.Completed;
            tournament.WinnerUserId = winnerUserId;
            tournament.Status = TournamentStatus.Completed;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            if (!wasCompleted)
            {
                await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
                await NotifyTournamentWinnerAsync(tournament);
            }
        }

        // Lazily creates the reset Grand Final when the Losers Bracket champion wins the first one.
        // Idempotent (a re-finalize that didn't first revert is a no-op). Links GF1.NextMatchId →
        // reset so the existing downstream-lock + revert machinery treats the reset as the round
        // that progressed past the Grand Final.
        private async Task CreateGrandFinalReset(MatchEntity grandFinal)
        {
            if (grandFinal.NextMatchId.HasValue) return;

            var reset = CreateMatch(
                grandFinal.TournamentId,
                grandFinal.TournamentStageId!.Value,
                (grandFinal.RoundNumber ?? 1) + 1,
                MatchStage.GrandFinalReset,
                0,
                DateTime.UtcNow);
            reset.HomeParticipantId = grandFinal.HomeParticipantId; // WB champion
            reset.AwayParticipantId = grandFinal.AwayParticipantId; // LB champion
            await this.AppUnitOfWork.MatchRepository.AddEntity(reset, this.UserContextReader);

            grandFinal.NextMatchId = reset.Id;
            grandFinal.NextMatchHomeAwaySlot = 0;
            await this.AppUnitOfWork.MatchRepository.UpdateEntity(grandFinal, this.UserContextReader);
        }

        private async Task<Guid?> ResolveParticipantUserId(Guid? participantId)
        {
            if (!participantId.HasValue) return null;
            var participant = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(participantId.Value);
            return participant.UserId;
        }

        private async Task AdvanceLoserToThirdPlace(MatchEntity match, Guid loserId, MatchEntity thirdPlaceMatch)
        {
            // Default (single-elim 3rd-place): semi-final order 0 loser → home, order 1 loser → away.
            // DE overrides this via NextMatchLoserBracketHomeAwaySlot — the WB loser always takes
            // the away slot of its target LB match (the home slot is reserved for the LB winner).
            bool isHomeSlot = match.NextMatchLoserBracketHomeAwaySlot.HasValue
                ? match.NextMatchLoserBracketHomeAwaySlot.Value == 0
                : (match.MatchOrder % 2) == 0;

            if (isHomeSlot)
                thirdPlaceMatch.HomeParticipantId = loserId;
            else
                thirdPlaceMatch.AwayParticipantId = loserId;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(thirdPlaceMatch, this.UserContextReader);
        }

        private async Task CheckAndCompleteLeague(Guid tournamentId)
        {
            var allMatchesFinished = await this.AppUnitOfWork.MatchRepository.AreAllMatchesFinishedInTournament(tournamentId);

            if (!allMatchesFinished)
                return;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);

            // For team leagues, also require all team matches to be Completed (sub-matches can be done before team-match finalization)
            if (tournament.IsTeamTournament)
            {
                var leagueStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 1);
                var teamMatches = await this.AppUnitOfWork.TeamMatchRepository.GetByStageId(leagueStage.Id!.Value);
                if (teamMatches.Any(tm => tm.Status != TeamMatchStatus.Completed))
                    return;
            }

            // The 5-minute standings cache may predate the result that just finished the league
            // (the bracket-wide invalidation in FinalizeMatchResult only runs after this method),
            // so drop it first — the winner must come from fresh data.
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");

            var tournamentStandings = await this.GetLeagueStandings(tournamentId);
            var winnerStanding = tournamentStandings.FirstOrDefault();

            if (tournament.IsTeamTournament)
            {
                if (winnerStanding != null)
                {
                    var winnerParticipant = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(winnerStanding.ParticipantId);
                    tournament.WinnerTeamId = winnerParticipant.TeamId;
                }
            }
            else
            {
                tournament.WinnerUserId = winnerStanding?.UserId ?? Guid.Empty;
            }

            // Re-edits of an already-completed tournament refresh the winner but must not
            // emit a second "tournament completed" activity / notification.
            bool wasCompleted = tournament.Status == TournamentStatus.Completed;
            tournament.Status = TournamentStatus.Completed;

            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            if (!wasCompleted)
            {
                await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
                await NotifyTournamentWinnerAsync(tournament);
            }
        }

        /// <summary>
        /// Swiss advance, called after every completed Swiss match (stats already resynced and
        /// saved): once the current round fully completes, either pairs the next round from the
        /// fresh standings or — after the configured number of rounds — completes the tournament
        /// with the standings leader as winner. Re-edits of older rounds are a no-op (the later
        /// round already exists), matching how league edits behave.
        /// </summary>
        private async Task CheckAndAdvanceSwissStage(MatchEntity completedMatch)
        {
            Guid tournamentId = completedMatch.TournamentId;
            Guid stageId = completedMatch.TournamentStageId!.Value;
            int completedRound = completedMatch.RoundNumber ?? 1;

            var stageMatches = await this.AppUnitOfWork.MatchRepository.GetByStageId(stageId);

            var currentRound = stageMatches.Where(m => (m.RoundNumber ?? 1) == completedRound).ToList();
            if (currentRound.Count == 0 || !currentRound.All(m => m.Status == MatchStatus.Completed))
                return;

            // Re-editing an old result must not pair a new round — later rounds already exist.
            // (Also narrows the race window when the round's last two results land concurrently.)
            if (stageMatches.Any(m => (m.RoundNumber ?? 1) > completedRound))
                return;

            var participants = completedMatch.TournamentGroupId.HasValue
                ? await this.AppUnitOfWork.TournamentParticipantRepository.GetByGroupId(completedMatch.TournamentGroupId.Value)
                : await this.AppUnitOfWork.TournamentParticipantRepository.GetForLeagueResync(tournamentId);

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);
            int totalRounds = GetSwissTotalRounds(participants.Count, tournament.SwissRoundsCount);

            // Buchholz from the freshly-resynced participant Points and the full match list of
            // this stage. Shared by the final-round branch (winner / post-stage seeding) and the
            // next-round pairing branch below.
            var opponentPointsSum = ComputeSwissOpponentPointsSum(participants, stageMatches);

            if (completedRound >= totalRounds)
            {
                var (knockoutSize, directBerths) = GetSwissKnockoutConfig(tournament);
                if (knockoutSize.HasValue)
                    await AdvanceSwissToPostStage(tournament, participants, knockoutSize.Value, directBerths!.Value, opponentPointsSum, stageMatches);
                else
                    await CompleteSwissTournament(tournament, participants, opponentPointsSum, stageMatches);
                return;
            }

            // Opponents already faced and byes already granted, derived from the match table.
            var playedPairs = new HashSet<(Guid, Guid)>();
            var byeCounts = new Dictionary<Guid, int>();
            foreach (var m in stageMatches)
            {
                if (m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue)
                    playedPairs.Add((m.HomeParticipantId.Value, m.AwayParticipantId.Value));
                else if (m.HomeParticipantId.HasValue)
                    byeCounts[m.HomeParticipantId.Value] = byeCounts.GetValueOrDefault(m.HomeParticipantId.Value) + 1;
            }

            var ordered = OrderForSwissStandings(participants, opponentPointsSum, stageMatches);

            var roundDuration = CalculateRoundDuration(tournament.RoundDurationMinutes);
            var (nextRoundMatches, byeParticipant) = BuildSwissRoundMatches(
                tournamentId,
                stageId,
                completedMatch.TournamentGroupId,
                completedRound + 1,
                ordered,
                playedPairs,
                byeCounts,
                roundDuration);

            foreach (var m in nextRoundMatches)
                await this.AppUnitOfWork.MatchRepository.AddEntity(m, this.UserContextReader);

            if (byeParticipant != null)
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(byeParticipant, this.UserContextReader);

            await this.SaveAsync();
        }

        /// <summary>
        /// Swiss rounds are done and a knockout is configured: freezes the final standings into
        /// Seed (1..n) and seeds the next stage — either the play-in round (ranks D+1 .. D+2K
        /// paired best-vs-worst) or, with no play-in, the knockout bracket itself (1 vs N,
        /// 2 vs N-1, …). Idempotent: once the post stage has matches, later Swiss edits only
        /// affect the displayed table — same semantics as group-stage edits after the draw.
        /// </summary>
        private async Task AdvanceSwissToPostStage(
            TournamentEntity tournament,
            List<TournamentParticipantEntity> participants,
            int knockoutSize,
            int directBerths,
            Dictionary<Guid, int> opponentPointsSum,
            IEnumerable<MatchEntity> matches)
        {
            Guid tournamentId = tournament.Id!.Value;
            bool hasPlayIn = directBerths < knockoutSize;

            var postStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 2);
            if (postStage == null) return;
            if (hasPlayIn)
            {
                if (postStage.Type != StageType.PlayIn) return;
            }
            else if (postStage.Type != StageType.SingleEliminationBracket
                     && postStage.Type != StageType.DoubleEliminationWinnersBracket)
            {
                // Direct-to-knockout: single-elim or the Winners-Bracket stage of a double-elim knockout.
                return;
            }

            if (await this.AppUnitOfWork.MatchRepository.HasMatchesForStage(postStage.Id!.Value)) return;

            // Freeze the qualification rank into Seed — it drives the play-in pairs and the
            // bracket slots, and the UI shows it next to every player.
            var ordered = OrderForSwissStandings(participants, opponentPointsSum, matches);
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Seed != i + 1)
                {
                    ordered[i].Seed = i + 1;
                    await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(ordered[i], this.UserContextReader);
                }
            }

            if (hasPlayIn)
            {
                int playInPairs = knockoutSize - directBerths;
                var pool = ordered.Skip(directBerths).Take(playInPairs * 2).ToList();

                // Guarded at bracket creation, but participants are the source of truth here.
                if (pool.Count < playInPairs * 2)
                    throw new Exception($"Play-in needs {playInPairs * 2} players below rank {directBerths}, found {pool.Count}.");

                DateTime openAt = DateTime.UtcNow;
                TimeSpan? duration = CalculateRoundDuration(tournament.RoundDurationMinutes);
                DateTime? deadline = duration.HasValue ? openAt + duration.Value : null;

                for (int i = 0; i < playInPairs; i++)
                {
                    var m = CreateMatch(tournamentId, postStage.Id!.Value, 1, MatchStage.PlayIn, i);
                    m.HomeParticipantId = pool[i].Id;
                    m.AwayParticipantId = pool[pool.Count - 1 - i].Id;
                    m.RoundOpenAt = openAt;
                    m.RoundDeadline = deadline;
                    await this.AppUnitOfWork.MatchRepository.AddEntity(m, this.UserContextReader);
                }
            }
            else
            {
                await GenerateSwissKnockoutMatches(tournament, postStage, ordered.Take(knockoutSize).ToList(), playInMatches: null);
            }

            await this.SaveAsync();
        }

        /// <summary>
        /// All play-in matches are done: the direct berths (standings 1..D) and the play-in
        /// winners (ordered by their own standings rank) form the knockout field, bracketed
        /// 1 vs N, 2 vs N-1, … Idempotent via the empty-knockout check.
        /// </summary>
        private async Task CheckAndAdvancePlayInStage(MatchEntity completedMatch)
        {
            Guid tournamentId = completedMatch.TournamentId;
            Guid playInStageId = completedMatch.TournamentStageId!.Value;

            var playInMatches = await this.AppUnitOfWork.MatchRepository.GetByStageId(playInStageId);
            if (playInMatches.Count == 0 || playInMatches.Any(m => m.Status != MatchStatus.Completed)) return;

            var knockoutStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 3);
            if (knockoutStage == null) return;
            if (knockoutStage.Type != StageType.SingleEliminationBracket
                && knockoutStage.Type != StageType.DoubleEliminationWinnersBracket) return;
            if (await this.AppUnitOfWork.MatchRepository.HasMatchesForStage(knockoutStage.Id!.Value)) return;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);
            var (knockoutSize, directBerths) = GetSwissKnockoutConfig(tournament);
            if (!knockoutSize.HasValue) return;

            // All participants (they live in the Swiss group); Seed holds the frozen
            // final-standings rank assigned by AdvanceSwissToPostStage.
            var participants = await this.AppUnitOfWork.TournamentParticipantRepository.GetEntitiesByTournamentId(tournamentId);
            var bySeed = participants.OrderBy(p => p.Seed ?? int.MaxValue).ToList();

            var winnerIds = playInMatches
                .Where(m => m.WinnerParticipantId.HasValue)
                .Select(m => m.WinnerParticipantId!.Value)
                .ToHashSet();

            var qualifiers = bySeed.Take(directBerths!.Value)
                .Concat(bySeed.Where(p => p.Id.HasValue && winnerIds.Contains(p.Id.Value)))
                .ToList();

            if (qualifiers.Count != knockoutSize.Value)
                throw new Exception($"Play-in produced {qualifiers.Count} qualifiers, expected {knockoutSize.Value}.");

            await GenerateSwissKnockoutMatches(tournament, knockoutStage, qualifiers, playInMatches);
            await this.SaveAsync();
        }

        // qualifiers[0] is the top seed. Standard bracket spread (1 vs N, 2 vs N-1, …), then each
        // play-in match gets its NextMatchId pointed at the R1 match its winner landed in, so the
        // ordinary revert / re-advance machinery covers play-in edits with no special-casing.
        private async Task GenerateSwissKnockoutMatches(
            TournamentEntity tournament,
            TournamentStageEntity knockoutStage,
            List<TournamentParticipantEntity> qualifiersInSeedOrder,
            List<MatchEntity>? playInMatches)
        {
            var seedOrder = GetStandardSeedOrder(qualifiersInSeedOrder.Count);
            var slots = seedOrder
                .Select(s => (TournamentParticipantEntity?)qualifiersInSeedOrder[s - 1])
                .ToList();

            // The knockout style is encoded in the stage created up-front: a Winners-Bracket stage
            // means the organizer chose double-elimination, with a Losers-Bracket stage right after it.
            bool useDouble = knockoutStage.Type == StageType.DoubleEliminationWinnersBracket;

            List<MatchEntity> matches;
            if (useDouble)
            {
                var lbStage = await this.AppUnitOfWork.TournamentStageRepository
                    .GetByOrder(tournament.Id!.Value, knockoutStage.Order + 1);
                if (lbStage == null || lbStage.Type != StageType.DoubleEliminationLosersBracket) return;

                matches = GenerateDoubleEliminationMatches(
                    tournament.Id!.Value, knockoutStage.Id!.Value, lbStage.Id!.Value, slots);
            }
            else
            {
                matches = GenerateEliminationMatches(
                    tournament.Id!.Value, knockoutStage.Id!.Value, slots, tournament.HasThirdPlaceMatch);
            }

            foreach (var m in matches)
                await this.AppUnitOfWork.MatchRepository.AddEntity(m, this.UserContextReader);

            if (playInMatches == null) return;

            // Play-in winners feed round 1 of the (winners) bracket. Restrict to the knockout stage
            // so LB round-1 matches (also RoundNumber 1, different stage) are never picked up.
            var firstRound = matches
                .Where(m => m.RoundNumber == 1 && m.TournamentStageId == knockoutStage.Id!.Value)
                .OrderBy(m => m.MatchOrder)
                .ToList();

            foreach (var playInMatch in playInMatches)
            {
                if (!playInMatch.WinnerParticipantId.HasValue) continue;

                for (int slot = 0; slot < slots.Count; slot++)
                {
                    if (slots[slot]?.Id != playInMatch.WinnerParticipantId) continue;

                    playInMatch.NextMatchId = firstRound[slot / 2].Id;
                    playInMatch.NextMatchHomeAwaySlot = slot % 2;
                    await this.AppUnitOfWork.MatchRepository.UpdateEntity(playInMatch, this.UserContextReader);
                    break;
                }
            }
        }

        // Winner comes straight from the freshly-resynced participant entities — deliberately
        // not GetLeagueStandings, whose 5-minute cache may predate the final result.
        private async Task CompleteSwissTournament(
            TournamentEntity tournament,
            List<TournamentParticipantEntity> participants,
            Dictionary<Guid, int> opponentPointsSum,
            IEnumerable<MatchEntity> matches)
        {
            var winner = OrderForSwissStandings(participants, opponentPointsSum, matches).FirstOrDefault();
            if (winner == null) return;

            // Re-edits of the final round refresh the winner but must not emit a second
            // "tournament completed" activity.
            bool wasCompleted = tournament.Status == TournamentStatus.Completed;

            tournament.WinnerUserId = winner.UserId;
            tournament.Status = TournamentStatus.Completed;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            await this.SaveAsync();

            if (!wasCompleted)
            {
                await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
                await NotifyTournamentWinnerAsync(tournament);
            }
        }

        // Standings order shared by Swiss pairing and winner selection. Tiebreaker chain:
        //   points → Buchholz (sum of opponents' points) → goal difference → head-to-head → goals for → random seed.
        // Buchholz is the standard Swiss-tournament tiebreaker — rewards facing tougher schedules.
        // Head-to-head (applied among rows tied on points+Buchholz+GD) is threaded in so the actual
        // qualification/winner order matches the displayed standings table — see BuildGroupStandings.
        // Byes contribute 0 (no opponent). `opponentPointsSum`/`matches` may be null for legacy callers
        // that don't have the stage's data at hand (those fall back to the shorter chain).
        private static List<TournamentParticipantEntity> OrderForSwissStandings(
            List<TournamentParticipantEntity> participants,
            Dictionary<Guid, int>? opponentPointsSum = null,
            IEnumerable<MatchEntity>? matches = null)
        {
            var ordered = participants
                .OrderByDescending(p => p.Points)
                .ThenByDescending(p => opponentPointsSum != null && p.Id.HasValue
                    ? opponentPointsSum.GetValueOrDefault(p.Id.Value)
                    : 0)
                .ThenByDescending(p => p.GoalsFor - p.GoalsAgainst)
                .ThenByDescending(p => p.GoalsFor)
                .ThenBy(p => p.Seed ?? 999)
                .ToList();

            // Head-to-head among players tied on points+Buchholz+GD — mirrors the displayed table.
            // Rows still tied after H2H keep their inbound order, so goals-for → seed remain the
            // final word (matching BuildGroupStandings, which falls back to goals-for → name).
            if (matches != null)
            {
                ResolveHeadToHeadInTiedChunks(
                    ordered,
                    p => p.Id!.Value,
                    p => (p.Points,
                          opponentPointsSum != null && p.Id.HasValue ? opponentPointsSum.GetValueOrDefault(p.Id.Value) : 0,
                          p.GoalsFor - p.GoalsAgainst),
                    BuildSoloH2HGames(matches));
            }

            return ordered;
        }

        // Buchholz score for every participant: sum of Points of every opponent actually played
        // (one contribution per completed match, so a forced rematch counts twice — standard
        // Buchholz). Byes (no away side) and still-pending matches contribute nothing: counting
        // a freshly-paired but unplayed opponent would bump everyone's OPP the moment a round is
        // drawn. In the advance path all stage matches are completed, so the filter is a no-op
        // there — it only corrects the mid-round standings display. O(matches + participants).
        private static Dictionary<Guid, int> ComputeSwissOpponentPointsSum(
            List<TournamentParticipantEntity> participants,
            IEnumerable<MatchEntity> matches)
        {
            var pointsById = participants
                .Where(p => p.Id.HasValue)
                .ToDictionary(p => p.Id!.Value, p => p.Points);

            var result = new Dictionary<Guid, int>(participants.Count);
            foreach (var p in participants)
                if (p.Id.HasValue) result[p.Id.Value] = 0;

            foreach (var m in matches)
            {
                if (m.Status != MatchStatus.Completed) continue;
                if (!m.HomeParticipantId.HasValue || !m.AwayParticipantId.HasValue) continue;
                var home = m.HomeParticipantId.Value;
                var away = m.AwayParticipantId.Value;
                if (pointsById.TryGetValue(away, out int awayPoints) && result.ContainsKey(home))
                    result[home] += awayPoints;
                if (pointsById.TryGetValue(home, out int homePoints) && result.ContainsKey(away))
                    result[away] += homePoints;
            }
            return result;
        }

        private async Task ProcessTeamMatchResult(MatchEntity completedSubMatch)
        {
            var teamMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(completedSubMatch.TeamMatchId!.Value);
            if (teamMatch == null) return;

            var subMatches = teamMatch.SubMatches;

            // Check if all sub-matches are completed
            if (!subMatches.All(sm => sm.Status == MatchStatus.Completed))
                return;

            // Atomic CAS: only one concurrent request can flip Status from Pending to Processing.
            // The losing request sees affected == 0 and exits without double-finalizing.
            bool claimed = await this.AppUnitOfWork.TeamMatchRepository.TryClaimForProcessing(teamMatch.Id!.Value);
            if (!claimed) return;

            // Sync local tracker with the value just written by ExecuteUpdate, so the next
            // SaveChanges doesn't try to write a stale Pending back.
            teamMatch.Status = TeamMatchStatus.Processing;

            try
            {
                await ProcessTeamMatchResultInner(completedSubMatch, teamMatch);
            }
            catch
            {
                // Release the claim so a retry (or revert + re-submit) can re-finalize this fixture.
                // Best-effort — if even the release fails the row stays in Processing, but that's
                // strictly safer than swallowing the original exception.
                try
                {
                    teamMatch.Status = TeamMatchStatus.Pending;
                    await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(teamMatch, this.UserContextReader);
                    await this.SaveAsync();
                }
                catch { /* let the original exception surface */ }

                throw;
            }
        }

        private async Task ProcessTeamMatchResultInner(MatchEntity completedSubMatch, TeamMatchEntity teamMatch)
        {
            var subMatches = teamMatch.SubMatches;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(teamMatch.TournamentId);
            var stageType = completedSubMatch.TournamentStage?.Type;
            bool isLeagueOrGroup = stageType == StageType.League || stageType == StageType.GroupStage;

            // Count wins per team and aggregate scores
            int homeWins = 0, awayWins = 0;
            int homeTotalScore = 0, awayTotalScore = 0;

            foreach (var sm in subMatches)
            {
                if (sm.WinnerParticipantId == sm.HomeParticipantId)
                    homeWins++;
                else if (sm.WinnerParticipantId == sm.AwayParticipantId)
                    awayWins++;

                homeTotalScore += sm.HomeUserScore ?? 0;
                awayTotalScore += sm.AwayUserScore ?? 0;
            }

            Guid? winnerTeamParticipantId = null;

            if (tournament.TeamWinCondition == TeamWinCondition.AggregateScore)
            {
                // Primary: aggregate score, tiebreaker: match wins
                if (homeTotalScore != awayTotalScore)
                {
                    winnerTeamParticipantId = homeTotalScore > awayTotalScore
                        ? teamMatch.HomeTeamParticipantId
                        : teamMatch.AwayTeamParticipantId;
                }
                else if (homeWins != awayWins)
                {
                    winnerTeamParticipantId = homeWins > awayWins
                        ? teamMatch.HomeTeamParticipantId
                        : teamMatch.AwayTeamParticipantId;
                }
            }
            else
            {
                // MatchWins: winner is determined solely by match wins — ties go straight to TieBreakRequired
                if (homeWins != awayWins)
                {
                    winnerTeamParticipantId = homeWins > awayWins
                        ? teamMatch.HomeTeamParticipantId
                        : teamMatch.AwayTeamParticipantId;
                }
            }

            // League/Group: draws are allowed, never TieBreakRequired
            if (isLeagueOrGroup)
            {
                teamMatch.WinnerTeamParticipantId = winnerTeamParticipantId;
                teamMatch.Status = TeamMatchStatus.Completed;
                await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(teamMatch, this.UserContextReader);

                var homePart = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(teamMatch.HomeTeamParticipantId!.Value);
                var awayPart = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(teamMatch.AwayTeamParticipantId!.Value);

                homePart.GoalsFor += homeTotalScore; homePart.GoalsAgainst += awayTotalScore;
                awayPart.GoalsFor += awayTotalScore; awayPart.GoalsAgainst += homeTotalScore;

                if (winnerTeamParticipantId == teamMatch.HomeTeamParticipantId)
                { homePart.Wins++; homePart.Points += 3; awayPart.Losses++; }
                else if (winnerTeamParticipantId == teamMatch.AwayTeamParticipantId)
                { awayPart.Wins++; awayPart.Points += 3; homePart.Losses++; }
                else
                { homePart.Draws++; homePart.Points += 1; awayPart.Draws++; awayPart.Points += 1; }

                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(homePart, this.UserContextReader);
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(awayPart, this.UserContextReader);

                await this.SaveAsync();

                await this.AppUnitOfWork.TournamentParticipantRepository.DetachEntity(homePart);
                await this.AppUnitOfWork.TournamentParticipantRepository.DetachEntity(awayPart);

                if (stageType == StageType.GroupStage)
                    await CheckAndAdvanceGroupStage(teamMatch.TournamentId, teamMatch.TournamentStageId!.Value);
                else
                    await CheckAndCompleteLeague(teamMatch.TournamentId);

                await cacheService.RemoveAsync($"bracket:{teamMatch.TournamentId}");
                await cacheService.RemoveAsync($"bracket:v3:{teamMatch.TournamentId}");
                await cacheService.RemoveAsync($"league_standings:{teamMatch.TournamentId}");
                await cacheService.RemoveAsync($"pdf:bracket:{teamMatch.TournamentId}");
                await cacheService.RemoveAsync($"tournament:{teamMatch.TournamentId}");
                return;
            }

            if (winnerTeamParticipantId == null)
            {
                // Both primary and tiebreaker are tied
                teamMatch.Status = TeamMatchStatus.TieBreakRequired;
                await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(teamMatch, this.UserContextReader);
                await this.SaveAsync();
                await cacheService.RemoveAsync($"bracket:{teamMatch.TournamentId}");
                await cacheService.RemoveAsync($"bracket:v3:{teamMatch.TournamentId}");
                await cacheService.RemoveAsync($"league_standings:{teamMatch.TournamentId}");
                await cacheService.RemoveAsync($"pdf:bracket:{teamMatch.TournamentId}");
                await cacheService.RemoveAsync($"tournament:{teamMatch.TournamentId}");
                return;
            }

            // Winner determined
            teamMatch.WinnerTeamParticipantId = winnerTeamParticipantId;
            teamMatch.Status = TeamMatchStatus.Completed;
            await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(teamMatch, this.UserContextReader);

            // Route the loser into its drop-in target: the third-place play-off (single-elim) or
            // the Losers Bracket match (double-elim). The slot override pins the DE destination
            // (a WB loser always lands in the away slot); single-elim leaves it null → MatchOrder%2.
            if (teamMatch.NextTeamMatchLoserBracketId.HasValue)
            {
                var loserTeamParticipantId = winnerTeamParticipantId == teamMatch.HomeTeamParticipantId
                    ? teamMatch.AwayTeamParticipantId
                    : teamMatch.HomeTeamParticipantId;

                if (loserTeamParticipantId.HasValue)
                {
                    var loserBracketMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(teamMatch.NextTeamMatchLoserBracketId.Value);

                    bool loserIsHomeSlot = teamMatch.NextTeamMatchLoserBracketHomeAwaySlot.HasValue
                        ? teamMatch.NextTeamMatchLoserBracketHomeAwaySlot.Value == 0
                        : (teamMatch.MatchOrder % 2) == 0;
                    if (loserIsHomeSlot)
                        loserBracketMatch.HomeTeamParticipantId = loserTeamParticipantId;
                    else
                        loserBracketMatch.AwayTeamParticipantId = loserTeamParticipantId;

                    await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(loserBracketMatch, this.UserContextReader);

                    // Once both sides are in, create the drop-in target's sub-matches.
                    if (loserBracketMatch.HomeTeamParticipantId.HasValue && loserBracketMatch.AwayTeamParticipantId.HasValue)
                        await CreateSubMatchesForTeamMatch(loserBracketMatch);
                }
            }

            // Advance or complete
            if (teamMatch.NextTeamMatchId.HasValue)
            {
                var nextTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(teamMatch.NextTeamMatchId.Value);

                bool isHomeSlot = teamMatch.NextTeamMatchHomeAwaySlot.HasValue
                    ? teamMatch.NextTeamMatchHomeAwaySlot.Value == 0
                    : (teamMatch.MatchOrder % 2) == 0;
                if (isHomeSlot)
                    nextTeamMatch.HomeTeamParticipantId = winnerTeamParticipantId;
                else
                    nextTeamMatch.AwayTeamParticipantId = winnerTeamParticipantId;

                await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(nextTeamMatch, this.UserContextReader);

                // If next team match now has both participants, create sub-matches for it
                if (nextTeamMatch.HomeTeamParticipantId.HasValue && nextTeamMatch.AwayTeamParticipantId.HasValue)
                {
                    await CreateSubMatchesForTeamMatch(nextTeamMatch);
                }
            }
            else if (teamMatch.IsGrandFinal)
            {
                // DE Grand Final. Home = WB champion (undefeated); away = LB champion (one loss). If
                // the LB champion wins, both now hold one loss → a single reset final decides the title.
                bool lbChampionWon = winnerTeamParticipantId == teamMatch.AwayTeamParticipantId;
                if (lbChampionWon)
                    await CreateTeamGrandFinalReset(teamMatch);
                else
                    await CompleteTeamTournament(tournament, winnerTeamParticipantId);
            }
            else if (teamMatch.IsGrandFinalReset)
            {
                // Reset final: its winner is the champion.
                await CompleteTeamTournament(tournament, winnerTeamParticipantId);
            }
            else
            {
                // Terminal team match: the championship final or the third-place play-off. With a third-place
                // play-off the tournament is finished only once BOTH are played, and the champion is always
                // the winner of the final (never the third-place match).
                var stageMatches = await this.AppUnitOfWork.TeamMatchRepository.GetByStageId(teamMatch.TournamentStageId!.Value);
                var finalTeamMatch = stageMatches.FirstOrDefault(tm => !tm.NextTeamMatchId.HasValue && !tm.IsThirdPlace);
                var thirdPlaceTeamMatch = stageMatches.FirstOrDefault(tm => tm.IsThirdPlace);

                // The in-flight match isn't saved as Completed yet, so treat it as done via the Id shortcut.
                bool finalDone = finalTeamMatch != null && (finalTeamMatch.Id == teamMatch.Id || finalTeamMatch.Status == TeamMatchStatus.Completed);
                bool thirdPlaceDone = thirdPlaceTeamMatch == null || thirdPlaceTeamMatch.Id == teamMatch.Id || thirdPlaceTeamMatch.Status == TeamMatchStatus.Completed;

                if (finalDone && thirdPlaceDone)
                {
                    var championParticipantId = finalTeamMatch!.Id == teamMatch.Id
                        ? winnerTeamParticipantId
                        : finalTeamMatch.WinnerTeamParticipantId;
                    var winnerParticipant = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(championParticipantId!.Value);

                    if (winnerParticipant.TeamId.HasValue)
                    {
                        tournament.WinnerTeamId = winnerParticipant.TeamId;
                    }

                    bool wasCompleted = tournament.Status == TournamentStatus.Completed;
                    tournament.Status = TournamentStatus.Completed;
                    await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
                    if (!wasCompleted)
                    {
                        await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
                        await NotifyTournamentWinnerAsync(tournament);
                    }
                }
            }

            await this.SaveAsync();

            await cacheService.RemoveAsync($"bracket:{teamMatch.TournamentId}");
            await cacheService.RemoveAsync($"bracket:v3:{teamMatch.TournamentId}");
            await cacheService.RemoveAsync($"league_standings:{teamMatch.TournamentId}");
            await cacheService.RemoveAsync($"pdf:bracket:{teamMatch.TournamentId}");
            await cacheService.RemoveAsync($"tournament:{teamMatch.TournamentId}");
        }

        private async Task CompleteTeamTournament(TournamentEntity tournament, Guid? championParticipantId)
        {
            if (championParticipantId.HasValue)
            {
                var winnerParticipant = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(championParticipantId.Value);
                if (winnerParticipant.TeamId.HasValue)
                    tournament.WinnerTeamId = winnerParticipant.TeamId;
            }

            bool wasCompleted = tournament.Status == TournamentStatus.Completed;
            tournament.Status = TournamentStatus.Completed;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            if (!wasCompleted)
            {
                await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
                await NotifyTournamentWinnerAsync(tournament);
            }
        }

        // Team mirror of CreateGrandFinalReset: lazily creates the reset Grand Final when the LB
        // champion wins the first one. Idempotent. Links GF1.NextTeamMatchId → reset so the existing
        // downstream-lock + revert machinery treats the reset as the round past the Grand Final, then
        // builds the reset's sub-matches (both finalists are already known).
        private async Task CreateTeamGrandFinalReset(TeamMatchEntity grandFinal)
        {
            if (grandFinal.NextTeamMatchId.HasValue) return;

            var reset = new TeamMatchEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = grandFinal.TournamentId,
                TournamentStageId = grandFinal.TournamentStageId,
                HomeTeamParticipantId = grandFinal.HomeTeamParticipantId, // WB champion
                AwayTeamParticipantId = grandFinal.AwayTeamParticipantId, // LB champion
                RoundNumber = (grandFinal.RoundNumber ?? 1) + 1,
                MatchOrder = 0,
                Status = TeamMatchStatus.Pending,
                IsUpperBracket = true,
                IsGrandFinalReset = true
            };
            await this.AppUnitOfWork.TeamMatchRepository.AddEntity(reset, this.UserContextReader);

            grandFinal.NextTeamMatchId = reset.Id;
            grandFinal.NextTeamMatchHomeAwaySlot = 0;
            await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(grandFinal, this.UserContextReader);

            await CreateSubMatchesForTeamMatch(reset);
        }

        private async Task CreateSubMatchesForTeamMatch(TeamMatchEntity teamMatch, int? teamSizeOverride = null)
        {
            var existing = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(teamMatch.Id!.Value);
            if (existing != null && existing.SubMatches.Count > 0)
                return;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(teamMatch.TournamentId);
            int teamSize = teamSizeOverride ?? tournament.TeamSize ?? 1;

            var homeMembers = await GetShuffledTeamMembers(teamMatch.HomeTeamParticipantId);
            var awayMembers = await GetShuffledTeamMembers(teamMatch.AwayTeamParticipantId);

            for (int j = 0; j < teamSize; j++)
            {
                var subMatch = CreateMatch(
                    teamMatch.TournamentId,
                    teamMatch.TournamentStageId!.Value,
                    teamMatch.RoundNumber ?? 1,
                    MatchStage.GroupStage,
                    (teamMatch.MatchOrder ?? 0) * teamSize + j);
                subMatch.TeamMatchId = teamMatch.Id;
                subMatch.HomeParticipantId = teamMatch.HomeTeamParticipantId;
                subMatch.AwayParticipantId = teamMatch.AwayTeamParticipantId;
                subMatch.HomeUserId = j < homeMembers.Count ? homeMembers[j].UserId : null;
                subMatch.AwayUserId = j < awayMembers.Count ? awayMembers[j].UserId : null;

                await this.AppUnitOfWork.MatchRepository.AddEntity(subMatch, this.UserContextReader);
            }
        }

        private async Task<List<TournamentTeamMemberEntity>> GetShuffledTeamMembers(Guid? teamParticipantId)
        {
            if (!teamParticipantId.HasValue)
                return new List<TournamentTeamMemberEntity>();

            var participant = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(teamParticipantId.Value);
            if (!participant.TeamId.HasValue)
                return new List<TournamentTeamMemberEntity>();

            var members = await this.AppUnitOfWork.TournamentTeamMemberRepository.GetByTeamId(participant.TeamId.Value);
            return members.Where(m => m.UserId.HasValue).OrderBy(_ => Guid.NewGuid()).ToList();
        }

        private async Task CheckAndAdvanceGroupStage(Guid tournamentId, Guid groupStageId)
        {
            var allGroupMatches = await this.AppUnitOfWork.MatchRepository.GetByStageId(groupStageId);

            if (allGroupMatches == null || !allGroupMatches.Any()) return;
            if (!allGroupMatches.All(m => m.Status == MatchStatus.Completed)) return;

            var knockoutStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 2);
            if (knockoutStage == null) return;

            // A Winners-Bracket stage at the knockout slot means the organizer chose double-elim
            // (solo only — the team path always creates a single-elim knockout stage).
            bool useDoubleKnockout = knockoutStage.Type == StageType.DoubleEliminationWinnersBracket;
            if (!useDoubleKnockout && knockoutStage.Type != StageType.SingleEliminationBracket) return;

            bool hasMatches = await this.AppUnitOfWork.MatchRepository.HasMatchesForStage(knockoutStage.Id!.Value);
            if (hasMatches) return;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);

            // Team matches feed the team-vs-team H2H tiebreaker below; loaded once and reused. For team
            // tournaments every team match must also be finalized before the bracket can be drawn.
            var stageTeamMatches = tournament.IsTeamTournament
                ? await this.AppUnitOfWork.TeamMatchRepository.GetByStageId(groupStageId)
                : new List<TeamMatchEntity>();
            if (tournament.IsTeamTournament && stageTeamMatches.Any(tm => tm.Status != TeamMatchStatus.Completed))
                return;

            var groupStage = await this.AppUnitOfWork.TournamentStageRepository.GetWithGroupsAndMatches(groupStageId);
            if (groupStage == null || groupStage.TournamentGroups == null) return;

            var qualifiers = new List<(TournamentParticipantEntity participant, int groupRank, string groupName)>();
            int qualifiersPerGroup = groupStage.QualifiedPlayersCount ?? 1;

            foreach (var group in groupStage.TournamentGroups.OrderBy(g => g.Name))
            {
                var groupParticipants = await this.AppUnitOfWork.TournamentParticipantRepository.GetByGroupIdWithNames(group.Id!.Value);

                var sorted = groupParticipants
                    .OrderByDescending(p => p.Points)
                    .ThenByDescending(p => p.GoalsFor - p.GoalsAgainst)
                    .ThenByDescending(p => p.GoalsFor)
                    .ThenBy(p => p.Team?.TeamName ?? p.User?.Username ?? p.UserId!.Value.ToString(), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // H2H between rows tied on (Points, GD): mini-table from the games played strictly
                // between the tied participants — team groups use the team-vs-team results, solo groups
                // the per-match scores. Decides knockout qualification before the alphabetical fallback.
                var groupParticipantIds = groupParticipants.Select(gp => gp.Id!.Value).ToHashSet();
                var h2hGames = tournament.IsTeamTournament
                    ? BuildTeamH2HGames(stageTeamMatches.Where(tm =>
                        tm.HomeTeamParticipantId.HasValue && groupParticipantIds.Contains(tm.HomeTeamParticipantId.Value)))
                    : BuildSoloH2HGames(allGroupMatches.Where(m => m.TournamentGroupId == group.Id));
                ResolveHeadToHeadInTiedChunks(
                    sorted,
                    p => p.Id!.Value,
                    p => (p.Points, 0, p.GoalsFor - p.GoalsAgainst),
                    h2hGames);

                for (int rank = 0; rank < Math.Min(qualifiersPerGroup, sorted.Count); rank++)
                {
                    qualifiers.Add((sorted[rank], rank + 1, group.Name));
                }
            }

            if (qualifiers.Count < 2) throw new Exception("Not enough qualifiers to create knockout bracket.");

            int totalQualifiers = qualifiers.Count;
            var rand = new Random();

            // The knockout bracket is padded up to the next power of two; the empty slots become byes
            // that the generators auto-advance. Seeds are handed out pot by pot (group winners first),
            // so the strongest qualifiers take the top seeds — and therefore the byes.
            int bracketSize = GetNextPowerOfTwo(totalQualifiers);
            var seedOrder = GetStandardSeedOrder(bracketSize);

            // Pots by group finish: pot 1 = group winners, pot 2 = runners-up, ... Within a pot every
            // team is from a different group, so the only same-group risk is a round-1 meeting between
            // teams of a different rank from the same group.
            var pots = qualifiers
                .GroupBy(q => q.groupRank)
                .OrderBy(g => g.Key)
                .Select(g => g.ToList())
                .ToList();

            // Try several random within-pot orderings and keep the bracket with the fewest round-1
            // same-group meetings (primary) and the best spread of each group's teams across the two
            // halves (secondary). Pot order is preserved, so winners always outrank runners-up.
            Dictionary<Guid, int> seedMap = null!;
            List<TournamentParticipantEntity?> bracketSlots = null!;
            int bestScore = int.MaxValue;

            for (int attempt = 0; attempt < 500; attempt++)
            {
                var seedToParticipant = new Dictionary<int, TournamentParticipantEntity>();
                int seed = 1;
                foreach (var pot in pots)
                    foreach (var q in pot.OrderBy(_ => rand.Next()))
                        seedToParticipant[seed++] = q.participant;

                var slots = seedOrder
                    .Select(s => seedToParticipant.TryGetValue(s, out var p) ? p : (TournamentParticipantEntity?)null)
                    .ToList();

                int round1Clashes = 0;
                for (int i = 0; i + 1 < slots.Count; i += 2)
                {
                    var home = slots[i];
                    var away = slots[i + 1];
                    if (home != null && away != null && home.TournamentGroupId == away.TournamentGroupId)
                        round1Clashes++;
                }

                int half = slots.Count / 2;
                var topGroups = slots.Take(half).Where(p => p != null).Select(p => p!.TournamentGroupId).ToList();
                var bottomGroups = slots.Skip(half).Where(p => p != null).Select(p => p!.TournamentGroupId).ToList();
                int halfClashes = (topGroups.Count - topGroups.Distinct().Count())
                                + (bottomGroups.Count - bottomGroups.Distinct().Count());

                // Round-1 separation dominates; the half spread only breaks ties between clean draws.
                int score = round1Clashes * 1000 + halfClashes;
                if (score < bestScore)
                {
                    bestScore = score;
                    bracketSlots = slots;
                    seedMap = seedToParticipant.ToDictionary(kv => kv.Value.Id!.Value, kv => kv.Key);
                }

                if (bestScore == 0) break;
            }

            foreach (var q in qualifiers)
            {
                var participantId = q.participant.Id!.Value;
                if (seedMap.TryGetValue(participantId, out int newSeed))
                {
                    // ResyncSoloLeagueStatistics ran earlier in this request and left
                    // the participants of the just-completed group tracked. DetachEntity
                    // by reference detaches the no-tracking copy, not the live tracked
                    // instance — so we have to clear by Id before attaching `fresh`.
                    await this.AppUnitOfWork.TournamentParticipantRepository.DetachById(participantId);
                    var fresh = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(participantId);
                    fresh.Seed = newSeed;
                    await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(fresh, this.UserContextReader);
                    q.participant.Seed = newSeed;
                }
            }

            await PopulateKnockoutFromSlots(tournament, tournamentId, knockoutStage, useDoubleKnockout, bracketSlots, rand);

            await this.SaveAsync();
        }

        // Builds the knockout fixtures (team or solo, single- or double-elim) from an ordered slot list
        // (nulls = byes) into the prepared knockout stage(s). Shared by the automatic group→knockout draw
        // and the manual re-seed regenerate path. The caller is responsible for SaveChanges.
        private async Task PopulateKnockoutFromSlots(
            TournamentEntity tournament,
            Guid tournamentId,
            TournamentStageEntity knockoutStage,
            bool useDoubleKnockout,
            List<TournamentParticipantEntity?> bracketSlots,
            Random rand)
        {
            int bracketSize = bracketSlots.Count;
            var realQualifiers = bracketSlots.Where(s => s != null).Select(s => s!).ToList();
            int totalQualifiers = realQualifiers.Count;

            if (tournament.IsTeamTournament)
            {
                int teamSize = tournament.TeamSize ?? 1;

                List<TeamMatchEntity> teamMatches;
                if (useDoubleKnockout)
                {
                    // Double-elim knockout: fill the WB + LB team stages created up-front (LB sits at the
                    // order right after the WB). bracketSlots may carry byes (non-power-of-two qualifier
                    // counts) — GenerateTeamDoubleEliminationMatches auto-advances the WB byes and
                    // collapses the orphaned LB matches.
                    var lbStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 3);
                    if (lbStage == null || lbStage.Type != StageType.DoubleEliminationLosersBracket) return;

                    teamMatches = GenerateTeamDoubleEliminationMatches(
                        tournamentId, knockoutStage.Id!.Value, lbStage.Id!.Value, bracketSlots);
                }
                else
                {
                    // bracketSlots already carries null bye slots; GenerateEliminationTeamMatches
                    // auto-advances them so the top seeds walk into the next round.
                    teamMatches = GenerateEliminationTeamMatches(tournamentId, knockoutStage.Id!.Value, bracketSlots);

                    if (tournament.HasThirdPlaceMatch)
                        BuildThirdPlaceTeamMatchIfApplicable(teamMatches, (int)Math.Log2(bracketSize), totalQualifiers, tournamentId, knockoutStage.Id);
                }

                foreach (var tm in teamMatches)
                    await this.AppUnitOfWork.TeamMatchRepository.AddEntity(tm, this.UserContextReader);

                var membersByParticipant = await BuildMembersByParticipantMap(realQualifiers);

                foreach (var tm in teamMatches)
                {
                    if (!tm.HomeTeamParticipantId.HasValue || !tm.AwayTeamParticipantId.HasValue) continue;
                    if (tm.Status == TeamMatchStatus.Completed) continue;

                    var subs = BuildSubMatchesForTeamMatch(tm, teamSize, null, membersByParticipant, rand);
                    foreach (var sm in subs)
                        await this.AppUnitOfWork.MatchRepository.AddEntity(sm, this.UserContextReader);
                }
            }
            else if (useDoubleKnockout)
            {
                // Solo double-elimination knockout: fill the Winners + Losers bracket stages that were
                // created up-front (LB sits at the order right after the WB). bracketSlots may carry byes
                // — GenerateDoubleEliminationMatches auto-advances the WB byes and collapses orphaned LB matches.
                var lbStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 3);
                if (lbStage == null || lbStage.Type != StageType.DoubleEliminationLosersBracket) return;

                var matches = GenerateDoubleEliminationMatches(
                    tournamentId, knockoutStage.Id!.Value, lbStage.Id!.Value, bracketSlots);

                foreach (var m in matches)
                    await this.AppUnitOfWork.MatchRepository.AddEntity(m, this.UserContextReader);
            }
            else
            {
                // Single-elim: bracketSlots already includes null bye slots; GenerateEliminationMatches
                // auto-advances them (leaving real first-round matches Pending), so don't reset status.
                var matches = GenerateEliminationMatches(tournamentId, knockoutStage.Id!.Value, bracketSlots, tournament.HasThirdPlaceMatch);

                foreach (var m in matches)
                    await this.AppUnitOfWork.MatchRepository.AddEntity(m, this.UserContextReader);
            }
        }

        private List<MatchEntity> GenerateEliminationMatches(Guid tournamentId, Guid stageId, List<TournamentParticipantEntity?> participants, bool includeThirdPlace = false)
        {
            int playerCount = participants.Count;
            int totalRounds = (int)Math.Log2(playerCount);
            var allMatches = new List<MatchEntity>();
            var currentRoundMatches = new List<MatchEntity>();
            int matchesInRound = playerCount / 2;

            for (int i = 0; i < matchesInRound; i++)
            {
                var match = CreateMatch(tournamentId, stageId, 1, GetMatchStage(playerCount, 1), i);
                match.HomeParticipantId = participants[i * 2]?.Id;
                match.AwayParticipantId = participants[i * 2 + 1]?.Id;
                currentRoundMatches.Add(match);
                allMatches.Add(match);
            }

            for (int round = 2; round <= totalRounds; round++)
            {
                matchesInRound /= 2;
                var nextRoundMatches = new List<MatchEntity>();
                for (int i = 0; i < matchesInRound; i++)
                {
                    var match = CreateMatch(tournamentId, stageId, round, GetMatchStage(playerCount, round), i);
                    currentRoundMatches[i * 2].NextMatchId = match.Id;
                    currentRoundMatches[i * 2 + 1].NextMatchId = match.Id;
                    nextRoundMatches.Add(match);
                    allMatches.Add(match);
                }
                currentRoundMatches = nextRoundMatches;
            }

            AutoAdvanceByes(allMatches);

            if (includeThirdPlace)
            {
                // A meaningful third-place play-off needs two semi-finals that each produce a real loser.
                // With fewer than 4 actual participants a semi-final would be a bye (no loser), so skip it.
                int realCount = participants.Count(p => p != null);
                var semiFinals = allMatches.Where(m => m.Stage == MatchStage.SemiFinal).ToList();
                var final = allMatches.FirstOrDefault(m => m.Stage == MatchStage.Final);

                if (realCount >= 4 && semiFinals.Count == 2 && final != null)
                {
                    // Shares the final's round, ordered after it; semi-final losers feed in via the loser-bracket link.
                    var thirdPlace = CreateMatch(tournamentId, stageId, final.RoundNumber!.Value, MatchStage.ThirdPlace, 1);
                    foreach (var sf in semiFinals)
                        sf.NextMatchLoserBracketId = thirdPlace.Id;
                    allMatches.Add(thirdPlace);
                }
            }

            return allMatches;
        }

        private List<MatchEntity> GenerateRoundRobinMatches(Guid tournamentId, Guid stageId, List<TournamentParticipantEntity> participants, bool doubleRoundRobin)
        {
            var allMatches = new List<MatchEntity>();
            int n = participants.Count;

            if (n == 2)
            {
                var m = CreateMatch(tournamentId, stageId, 1, MatchStage.GroupStage, 0);
                m.HomeParticipantId = participants[0].Id;
                m.AwayParticipantId = participants[1].Id;
                allMatches.Add(m);

                if (doubleRoundRobin)
                {
                    var ret = CreateMatch(tournamentId, stageId, 2, MatchStage.GroupStage, 1);
                    ret.HomeParticipantId = participants[1].Id;
                    ret.AwayParticipantId = participants[0].Id;
                    allMatches.Add(ret);
                }

                return allMatches;
            }

            bool hasBye = n % 2 != 0;
            if (hasBye) n++;

            int totalRounds = n - 1;
            int matchesPerRound = n / 2;

            // Cleaner initialization of circle list
            var circle = new List<TournamentParticipantEntity>(participants);
            if (hasBye) circle.Add(null!);

            int matchOrder = 0;

            for (int round = 1; round <= totalRounds; round++)
            {
                for (int match = 0; match < matchesPerRound; match++)
                {
                    var homeP = circle[match];
                    var awayP = circle[n - 1 - match];

                    if (homeP != null && awayP != null)
                    {
                        var m = CreateMatch(tournamentId, stageId, round, MatchStage.GroupStage, matchOrder++);
                        m.HomeParticipantId = homeP.Id;
                        m.AwayParticipantId = awayP.Id;
                        allMatches.Add(m);
                    }
                }

                if (round < totalRounds)
                {
                    var temp = circle[n - 1];
                    for (int i = n - 1; i > 1; i--)
                        circle[i] = circle[i - 1];
                    circle[1] = temp;
                }
            }

            if (doubleRoundRobin)
            {
                int firstRoundMatchCount = allMatches.Count;
                for (int i = 0; i < firstRoundMatchCount; i++)
                {
                    var orig = allMatches[i];
                    var ret = CreateMatch(tournamentId, stageId, orig.RoundNumber!.Value + totalRounds, MatchStage.GroupStage, matchOrder++);
                    ret.HomeParticipantId = orig.AwayParticipantId;
                    ret.AwayParticipantId = orig.HomeParticipantId;
                    allMatches.Add(ret);
                }
            }

            return allMatches;
        }

        private List<TeamMatchEntity> GenerateRoundRobinTeamMatches(Guid tournamentId, Guid stageId, List<TournamentParticipantEntity> participants, bool doubleRoundRobin)
        {
            var all = new List<TeamMatchEntity>();
            int n = participants.Count;

            if (n == 2)
            {
                var tm = CreateTeamMatch(tournamentId, stageId, 1, 0);
                tm.HomeTeamParticipantId = participants[0].Id;
                tm.AwayTeamParticipantId = participants[1].Id;
                all.Add(tm);

                if (doubleRoundRobin)
                {
                    var ret = CreateTeamMatch(tournamentId, stageId, 2, 1);
                    ret.HomeTeamParticipantId = participants[1].Id;
                    ret.AwayTeamParticipantId = participants[0].Id;
                    all.Add(ret);
                }

                return all;
            }

            bool hasBye = n % 2 != 0;
            if (hasBye) n++;

            int totalRounds = n - 1;
            int matchesPerRound = n / 2;

            var circle = new List<TournamentParticipantEntity>(participants);
            if (hasBye) circle.Add(null!);

            int matchOrder = 0;

            for (int round = 1; round <= totalRounds; round++)
            {
                for (int match = 0; match < matchesPerRound; match++)
                {
                    var homeP = circle[match];
                    var awayP = circle[n - 1 - match];

                    if (homeP != null && awayP != null)
                    {
                        var tm = CreateTeamMatch(tournamentId, stageId, round, matchOrder++);
                        tm.HomeTeamParticipantId = homeP.Id;
                        tm.AwayTeamParticipantId = awayP.Id;
                        all.Add(tm);
                    }
                }

                if (round < totalRounds)
                {
                    var temp = circle[n - 1];
                    for (int i = n - 1; i > 1; i--)
                        circle[i] = circle[i - 1];
                    circle[1] = temp;
                }
            }

            if (doubleRoundRobin)
            {
                int firstRoundCount = all.Count;
                for (int i = 0; i < firstRoundCount; i++)
                {
                    var orig = all[i];
                    var ret = CreateTeamMatch(tournamentId, stageId, orig.RoundNumber!.Value + totalRounds, matchOrder++);
                    ret.HomeTeamParticipantId = orig.AwayTeamParticipantId;
                    ret.AwayTeamParticipantId = orig.HomeTeamParticipantId;
                    all.Add(ret);
                }
            }

            return all;
        }

        private List<TeamMatchEntity> GenerateEliminationTeamMatches(Guid tournamentId, Guid stageId, List<TournamentParticipantEntity?> participants)
        {
            int playerCount = participants.Count;
            int totalRounds = (int)Math.Log2(playerCount);
            var all = new List<TeamMatchEntity>();
            var currentRound = new List<TeamMatchEntity>();
            int matchesInRound = playerCount / 2;

            for (int i = 0; i < matchesInRound; i++)
            {
                var tm = new TeamMatchEntity
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournamentId,
                    TournamentStageId = stageId,
                    HomeTeamParticipantId = participants[i * 2]?.Id,
                    AwayTeamParticipantId = participants[i * 2 + 1]?.Id,
                    RoundNumber = 1,
                    MatchOrder = i,
                    Status = TeamMatchStatus.Pending
                };
                currentRound.Add(tm);
                all.Add(tm);
            }

            for (int round = 2; round <= totalRounds; round++)
            {
                matchesInRound /= 2;
                var next = new List<TeamMatchEntity>();
                for (int i = 0; i < matchesInRound; i++)
                {
                    var tm = new TeamMatchEntity
                    {
                        Id = Guid.NewGuid(),
                        TournamentId = tournamentId,
                        TournamentStageId = stageId,
                        RoundNumber = round,
                        MatchOrder = i,
                        Status = TeamMatchStatus.Pending
                    };
                    currentRound[i * 2].NextTeamMatchId = tm.Id;
                    currentRound[i * 2 + 1].NextTeamMatchId = tm.Id;
                    next.Add(tm);
                    all.Add(tm);
                }
                currentRound = next;
            }

            AutoAdvanceTeamByes(all);
            return all;
        }

        private TeamMatchEntity CreateTeamMatch(Guid tournamentId, Guid stageId, int round, int order)
        {
            return new TeamMatchEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                TournamentStageId = stageId,
                RoundNumber = round,
                MatchOrder = order,
                Status = TeamMatchStatus.Pending
            };
        }

        private List<MatchEntity> BuildSubMatchesForTeamMatch(
            TeamMatchEntity teamMatch,
            int teamSize,
            Guid? groupId,
            Dictionary<Guid, List<TournamentTeamMemberEntity>> membersByParticipant,
            Random rand)
        {
            var homeMembers = ShuffleMembersFromMap(teamMatch.HomeTeamParticipantId, membersByParticipant, rand);
            var awayMembers = ShuffleMembersFromMap(teamMatch.AwayTeamParticipantId, membersByParticipant, rand);

            var list = new List<MatchEntity>();
            for (int j = 0; j < teamSize; j++)
            {
                var sm = CreateMatch(
                    teamMatch.TournamentId,
                    teamMatch.TournamentStageId!.Value,
                    teamMatch.RoundNumber ?? 1,
                    MatchStage.GroupStage,
                    (teamMatch.MatchOrder ?? 0) * teamSize + j);
                sm.TeamMatchId = teamMatch.Id;
                sm.HomeParticipantId = teamMatch.HomeTeamParticipantId;
                sm.AwayParticipantId = teamMatch.AwayTeamParticipantId;
                sm.HomeUserId = j < homeMembers.Count ? homeMembers[j].UserId : null;
                sm.AwayUserId = j < awayMembers.Count ? awayMembers[j].UserId : null;
                if (groupId.HasValue) sm.TournamentGroupId = groupId;
                list.Add(sm);
            }
            return list;
        }

        private static List<TournamentTeamMemberEntity> ShuffleMembersFromMap(
            Guid? participantId,
            Dictionary<Guid, List<TournamentTeamMemberEntity>> map,
            Random rand)
        {
            if (!participantId.HasValue) return new List<TournamentTeamMemberEntity>();
            return map.TryGetValue(participantId.Value, out var members)
                ? members.Where(m => m.UserId.HasValue).OrderBy(_ => rand.Next()).ToList()
                : new List<TournamentTeamMemberEntity>();
        }

        private async Task<Dictionary<Guid, List<TournamentTeamMemberEntity>>> BuildMembersByParticipantMap(IEnumerable<TournamentParticipantEntity> participants)
        {
            // participantId -> teamId
            var teamIdByParticipant = participants
                .Where(p => p.Id.HasValue && p.TeamId.HasValue)
                .ToDictionary(p => p.Id!.Value, p => p.TeamId!.Value);

            var teamIds = teamIdByParticipant.Values.Distinct().ToList();
            if (teamIds.Count == 0)
                return new Dictionary<Guid, List<TournamentTeamMemberEntity>>();

            var allMembers = await this.AppUnitOfWork.TournamentTeamMemberRepository.GetByTeamIds(teamIds);
            var membersByTeam = allMembers
                .Where(m => m.TeamId.HasValue)
                .GroupBy(m => m.TeamId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = new Dictionary<Guid, List<TournamentTeamMemberEntity>>();
            foreach (var (participantId, teamId) in teamIdByParticipant)
            {
                result[participantId] = membersByTeam.TryGetValue(teamId, out var list) ? list : new List<TournamentTeamMemberEntity>();
            }
            return result;
        }

        private List<TournamentParticipantEntity> GetStandardBracketSeeding(List<TournamentParticipantEntity> participants)
        {
            var sorted = participants.OrderBy(x => x.Seed ?? 999).ToList();
            int n = sorted.Count;
            var bracketOrder = new List<int> { 0 };
            int count = 1;
            while (count < n)
            {
                var newOrder = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    newOrder.Add(bracketOrder[i]);
                    newOrder.Add(count * 2 - 1 - bracketOrder[i]);
                }
                bracketOrder = newOrder;
                count *= 2;
            }
            // F12: when n isn't a power of two, bracketOrder grows to the next power of two and contains
            // indices >= n. Those positions are byes — skip them instead of indexing sorted[i] out of
            // range (the previous behaviour threw). Callers pad the remaining slots with byes.
            return bracketOrder.Where(i => i < n).Select(i => sorted[i]).ToList();
        }

        private MatchEntity CreateMatch(Guid tournamentId, Guid stageId, int round, MatchStage stage, int order, DateTime? roundOpenAt = null)
        {
            return new MatchEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                TournamentStageId = stageId,
                RoundNumber = round,
                Stage = stage,
                MatchOrder = order,
                Status = MatchStatus.Pending,
                IsUpperBracket = true,
                RoundOpenAt = roundOpenAt
            };
        }

        private static MatchStage GetMatchStage(int totalPlayers, int roundNumber)
            => StageFromRoundsFromEnd((int)Math.Log2(totalPlayers) - roundNumber + 1);

        // Creates the optional third-place TeamMatch and links both semi-finals' loser pointer into it.
        // Shared between pure single-elimination and groups-then-knockout team paths to keep generation logic in one place.
        // Returns null when the play-off is not applicable (too few real teams, or no two semi-finals).
        private static TeamMatchEntity? BuildThirdPlaceTeamMatchIfApplicable(
            List<TeamMatchEntity> allTeamMatches,
            int totalRounds,
            int realParticipantCount,
            Guid tournamentId,
            Guid? stageId)
        {
            // A meaningful third-place play-off needs two semi-finals that each produce a real loser (no bye).
            if (realParticipantCount < 4 || totalRounds < 2) return null;

            var semiFinals = allTeamMatches.Where(tm => tm.RoundNumber == totalRounds - 1).ToList();
            var final = allTeamMatches.FirstOrDefault(tm => tm.RoundNumber == totalRounds);

            if (semiFinals.Count != 2 || final == null) return null;

            var thirdPlace = new TeamMatchEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                TournamentStageId = stageId,
                RoundNumber = totalRounds,
                MatchOrder = 1,
                Status = TeamMatchStatus.Pending,
                IsThirdPlace = true
            };
            foreach (var sf in semiFinals)
                sf.NextTeamMatchLoserBracketId = thirdPlace.Id;

            allTeamMatches.Add(thirdPlace);
            return thirdPlace;
        }

        // Shared between solo (stage stored on MatchEntity at generation) and team (derived at DTO mapping,
        // since TeamMatchEntity has no Stage column). Keeps the mapping in one place.
        private static MatchStage StageFromRoundsFromEnd(int roundsFromEnd) => roundsFromEnd switch
        {
            1 => MatchStage.Final,
            2 => MatchStage.SemiFinal,
            3 => MatchStage.QuarterFinal,
            4 => MatchStage.RoundOf16,
            5 => MatchStage.RoundOf32,
            6 => MatchStage.RoundOf64,
            7 => MatchStage.RoundOf128,
            8 => MatchStage.RoundOf256,
            9 => MatchStage.RoundOf512,
            _ => MatchStage.RoundOf1024
        };

        private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

        private static int GetNextPowerOfTwo(int n)
        {
            int power = 1;
            while (power < n) power <<= 1;
            return power;
        }

        private static List<int> GetStandardSeedOrder(int bracketSize)
        {
            var order = new List<int> { 1 };
            int count = 1;

            while (count < bracketSize)
            {
                var next = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    next.Add(order[i]);
                    next.Add((count * 2) + 1 - order[i]);
                }
                order = next;
                count *= 2;
            }

            return order;
        }

        private static void AutoAdvanceByes(List<MatchEntity> allMatches)
        {
            var matchesById = allMatches
                .Where(m => m.Id.HasValue)
                .ToDictionary(m => m.Id!.Value, m => m);

            var firstRound = allMatches
                .Where(m => (m.RoundNumber ?? 1) == 1)
                .OrderBy(m => m.MatchOrder)
                .ToList();

            foreach (var match in firstRound)
            {
                bool hasHome = match.HomeParticipantId.HasValue;
                bool hasAway = match.AwayParticipantId.HasValue;

                if (hasHome == hasAway)
                {
                    if (!hasHome) match.Status = MatchStatus.Completed;
                    continue;
                }

                var winnerId = match.HomeParticipantId ?? match.AwayParticipantId;
                match.WinnerParticipantId = winnerId;
                match.Status = MatchStatus.Completed;

                if (winnerId.HasValue && match.NextMatchId.HasValue && matchesById.TryGetValue(match.NextMatchId.Value, out var nextMatch))
                {
                    bool isHomeSlot = (match.MatchOrder % 2) == 0;
                    if (isHomeSlot) nextMatch.HomeParticipantId = winnerId;
                    else nextMatch.AwayParticipantId = winnerId;
                }
            }
        }

        private async Task CheckAndUnlockNextRound(Guid tournamentId, Guid stageId, int completedRound)
        {
            var stageMatches = await this.AppUnitOfWork.MatchRepository.GetByStageId(stageId);

            var currentRound = stageMatches.Where(m => m.RoundNumber == completedRound).ToList();
            if (!currentRound.All(m => m.Status == MatchStatus.Completed)) return;

            var nextRoundMatches = stageMatches.Where(m => m.RoundNumber == completedRound + 1).ToList();
            if (nextRoundMatches.Count == 0) return;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);
            var roundDuration = CalculateRoundDuration(tournament.RoundDurationMinutes);

            foreach (var m in nextRoundMatches)
            {
                m.RoundOpenAt = DateTime.UtcNow;

                if (roundDuration.HasValue && !m.RoundDeadline.HasValue)
                    m.RoundDeadline = DateTime.UtcNow + roundDuration.Value;

                await this.AppUnitOfWork.MatchRepository.UpdateEntity(m, this.UserContextReader);
            }

            await this.SaveAsync();
        }

        private static TimeSpan? CalculateRoundDuration(int? minutes)
            => minutes.HasValue ? TimeSpan.FromMinutes(minutes.Value) : null;

        /// <summary>
        /// Assigns RoundOpenAt and RoundDeadline for all rounds upfront at generation time.
        /// If roundDuration is null: Round 1 opens immediately, all other rounds are locked (DateTime.MaxValue).
        /// If roundDuration has value: Each round opens sequentially based on duration * roundIndex.
        /// </summary>
        private static void AssignAllRoundSchedules(List<MatchEntity> matches, TimeSpan? roundDuration)
        {
            if (!roundDuration.HasValue)
            {
                foreach (var m in matches)
                    m.RoundOpenAt = (m.RoundNumber ?? 1) == 1 ? DateTime.UtcNow : DateTime.MaxValue;
                return;
            }

            var roundGroups = matches
                .GroupBy(m => m.RoundNumber ?? 1)
                .OrderBy(g => g.Key);

            foreach (var group in roundGroups)
            {
                int roundIndex = group.Key - 1;
                DateTime openAt = DateTime.UtcNow.AddMinutes(roundDuration.Value.TotalMinutes * roundIndex);
                DateTime deadline = openAt + roundDuration.Value;

                foreach (var m in group)
                {
                    m.RoundOpenAt = openAt;
                    m.RoundDeadline = deadline;
                }
            }
        }

        private bool IsElimination(StageType? type)
            => type == StageType.SingleEliminationBracket
            || type == StageType.DoubleEliminationWinnersBracket
            || type == StageType.DoubleEliminationLosersBracket
            || type == StageType.PlayIn;

        private string GetGroupName(int index)
        {
            const string l = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            return index < l.Length ? l[index].ToString() : (index + 1).ToString();
        }

        private List<BracketRoundDto> MapTeamBracketRounds(List<TeamMatchEntity>? teamMatches, bool dropCollapsedByes = false)
        {
            var rounds = new List<BracketRoundDto>();
            if (teamMatches == null || !teamMatches.Any()) return rounds;

            if (dropCollapsedByes)
            {
                // A collapsed LB bye is a Completed team match with no participants and no winner —
                // the DE LB cascade flips its status when both upstream feeders turned out to be byes.
                // Strip them so the LB tab shows only matches that actually get played.
                teamMatches = teamMatches
                    .Where(m => !(m.Status == TeamMatchStatus.Completed
                                  && !m.HomeTeamParticipantId.HasValue
                                  && !m.AwayTeamParticipantId.HasValue
                                  && !m.WinnerTeamParticipantId.HasValue))
                    .ToList();

                if (teamMatches.Count == 0) return rounds;
            }

            // Exclude the Grand Final (one round past the WB final) so WB rounds keep their
            // Final/Semi/... labels — the GF is mapped explicitly via IsGrandFinal. The third-place
            // play-off shares the final's RoundNumber, so Max over the rest gives the true tree depth.
            int totalRounds = teamMatches.Where(m => !m.IsGrandFinal && !m.IsGrandFinalReset)
                                         .Select(m => m.RoundNumber ?? 1)
                                         .DefaultIfEmpty(1)
                                         .Max();

            var grouped = teamMatches.GroupBy(m => m.RoundNumber ?? 1).OrderBy(g => g.Key);

            foreach (var grp in grouped)
            {
                rounds.Add(new BracketRoundDto
                {
                    RoundNumber = grp.Key,
                    RoundDeadline = grp.SelectMany(m => m.SubMatches).Max(sm => sm.RoundDeadline),
                    Name = $"Round {grp.Key}",
                    Matches = grp.OrderBy(m => m.MatchOrder)
                                 .Select(tm => MapTeamMatchToDto(tm, totalRounds))
                                 .ToList()
                });
            }
            return rounds;
        }

        private MatchStructureDto MapTeamMatchToDto(TeamMatchEntity tm, int totalRounds)
        {
            int round = tm.RoundNumber ?? 1;
            return new MatchStructureDto
            {
                Id = tm.Id!.Value,
                Round = round,
                Order = tm.MatchOrder ?? 0,
                Stage = tm.IsGrandFinalReset ? MatchStage.GrandFinalReset
                    : tm.IsGrandFinal ? MatchStage.GrandFinal
                    : !tm.IsUpperBracket ? MatchStage.LosersBracket
                    : tm.IsThirdPlace ? MatchStage.ThirdPlace
                    : StageFromRoundsFromEnd(totalRounds - round + 1),
                Status = MapTeamMatchStatus(tm.Status),
                TeamMatchId = tm.Id,
                IsUpperBracket = tm.IsUpperBracket,
                NextTeamMatchId = tm.NextTeamMatchId,
                NextTeamMatchLoserBracketId = tm.NextTeamMatchLoserBracketId,
                Evidences = [],
                Home = tm.HomeTeamParticipant == null ? null : new MatchParticipantDto
                {
                    ParticipantId = tm.HomeTeamParticipant.Id!.Value,
                    UserId = tm.HomeTeamParticipant.UserId ?? Guid.Empty,
                    Username = tm.HomeTeamParticipant.Team?.TeamName ?? "Unknown",
                    TeamName = tm.HomeTeamParticipant.Team?.TeamName,
                    IsWinner = tm.WinnerTeamParticipantId == tm.HomeTeamParticipant.Id
                },
                Away = tm.AwayTeamParticipant == null ? null : new MatchParticipantDto
                {
                    ParticipantId = tm.AwayTeamParticipant.Id!.Value,
                    UserId = tm.AwayTeamParticipant.UserId ?? Guid.Empty,
                    Username = tm.AwayTeamParticipant.Team?.TeamName ?? "Unknown",
                    TeamName = tm.AwayTeamParticipant.Team?.TeamName,
                    IsWinner = tm.WinnerTeamParticipantId == tm.AwayTeamParticipant.Id
                }
            };
        }

        private static MatchStatus MapTeamMatchStatus(TeamMatchStatus status)
            => status switch
            {
                TeamMatchStatus.Completed => MatchStatus.Completed,
                _ => MatchStatus.Pending
            };

        // Group-stage team match → card. Like MapTeamMatchToDto but tags Stage as GroupStage and
        // pulls per-match schedule (start / deadline / round lock) from the sub-matches, which is
        // where a team match's round timing actually lives (TeamMatchEntity has no schedule columns).
        private MatchStructureDto MapGroupTeamMatchToDto(TeamMatchEntity tm)
        {
            var subs = tm.SubMatches ?? new List<MatchEntity>();
            var anySub = subs.FirstOrDefault();

            // Team match score = sub-matches won by each side (same tally the match-details
            // AggregateScore uses). Only surfaced once the team match is decided, so unplayed
            // cards keep showing "—" instead of a misleading 0 : 0.
            int? homeScore = null, awayScore = null;
            if (tm.Status == TeamMatchStatus.Completed)
            {
                int h = 0, a = 0;
                foreach (var sm in subs)
                {
                    if (sm.Status == MatchStatus.Completed && sm.WinnerParticipantId.HasValue)
                    {
                        if (sm.WinnerParticipantId == tm.HomeTeamParticipantId) h++;
                        else if (sm.WinnerParticipantId == tm.AwayTeamParticipantId) a++;
                    }
                }
                homeScore = h;
                awayScore = a;
            }

            return new MatchStructureDto
            {
                Id = tm.Id!.Value,
                Round = tm.RoundNumber ?? 1,
                Order = tm.MatchOrder ?? 0,
                Stage = MatchStage.GroupStage,
                Status = MapTeamMatchStatus(tm.Status),
                TeamMatchId = tm.Id,
                StartTime = anySub?.ScheduledStartTime,
                RoundDeadline = subs.Count > 0 ? subs.Max(s => s.RoundDeadline) : null,
                IsRoundLocked = anySub?.RoundOpenAt.HasValue == true && anySub.RoundOpenAt!.Value > DateTime.UtcNow,
                MatchOpensAt = anySub?.RoundOpenAt,
                Evidences = [],
                Home = tm.HomeTeamParticipant == null ? null : new MatchParticipantDto
                {
                    ParticipantId = tm.HomeTeamParticipant.Id!.Value,
                    UserId = tm.HomeTeamParticipant.UserId ?? Guid.Empty,
                    Username = tm.HomeTeamParticipant.Team?.TeamName ?? "Unknown",
                    TeamName = tm.HomeTeamParticipant.Team?.TeamName,
                    Score = homeScore,
                    IsWinner = tm.WinnerTeamParticipantId == tm.HomeTeamParticipant.Id
                },
                Away = tm.AwayTeamParticipant == null ? null : new MatchParticipantDto
                {
                    ParticipantId = tm.AwayTeamParticipant.Id!.Value,
                    UserId = tm.AwayTeamParticipant.UserId ?? Guid.Empty,
                    Username = tm.AwayTeamParticipant.Team?.TeamName ?? "Unknown",
                    TeamName = tm.AwayTeamParticipant.Team?.TeamName,
                    Score = awayScore,
                    IsWinner = tm.WinnerTeamParticipantId == tm.AwayTeamParticipant.Id
                }
            };
        }

        private List<BracketRoundDto> MapBracketRounds(List<MatchEntity>? matches, Guid currentUserId, bool isPrivileged, bool dropCollapsedByes = false)
        {
            var rounds = new List<BracketRoundDto>();
            if (matches == null || !matches.Any()) return rounds;

            if (dropCollapsedByes)
            {
                // A collapsed bye is a Completed match with no participants and no winner —
                // the DE LB cascade flips its status when both upstream feeders turned out to
                // be byes. We strip them here so the LB tab shows only matches that actually
                // get played; the cascade has already re-routed the routing for what remains.
                matches = matches
                    .Where(m => !(m.Status == MatchStatus.Completed
                                  && !m.HomeParticipantId.HasValue
                                  && !m.AwayParticipantId.HasValue
                                  && !m.WinnerParticipantId.HasValue))
                    .ToList();

                if (matches.Count == 0) return rounds;
            }

            var matchById = matches
                .Where(m => m.Id.HasValue)
                .ToDictionary(m => m.Id!.Value, m => m);

            var grouped = matches.GroupBy(m => m.RoundNumber ?? 1).OrderBy(g => g.Key);

            foreach (var grp in grouped)
            {
                rounds.Add(new BracketRoundDto
                {
                    RoundNumber = grp.Key,
                    Name = $"Round {grp.Key}",
                    RoundDeadline = grp.Max(m => m.RoundDeadline),
                    Matches = grp.OrderBy(m => m.MatchOrder)
                                 .Select(m => MapMatchToDto(m, currentUserId, isPrivileged, matchById))
                                 .ToList()
                });
            }
            return rounds;
        }

        private async Task<List<GroupDto>> MapGroups(TournamentStageEntity stage, Guid currentUserId, bool isPrivileged, bool teamGroupMatches = false)
        {
            var groupDtos = new List<GroupDto>();
            var groups = stage.TournamentGroups ?? new List<TournamentGroupEntity>();

            if (groups.Count == 0)
                return groupDtos;

            // Single query for all participants across all groups (eliminates N+1)
            var groupIds = groups.Select(g => g.Id!.Value).ToList();
            var allParticipants = await this.AppUnitOfWork.TournamentParticipantRepository.GetByGroupIdsWithNames(groupIds);
            var participantsByGroup = allParticipants
                .GroupBy(p => p.TournamentGroupId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Team group stages keep the team-vs-team matches in TeamMatches; the per-player
            // sub-matches in stage.Matches are only used here for standings/deadline timing.
            // Gated behind teamGroupMatches (v3 only) so v1/v2 keep emitting per-player sub-match
            // cards byte-identically for the live app.
            bool isTeamGroup = teamGroupMatches && stage.TeamMatches != null && stage.TeamMatches.Count > 0;

            foreach (var group in groups)
            {
                var groupMatches = stage.Matches?
                    .Where(m => m.TournamentGroupId == group.Id)
                    .OrderBy(m => m.RoundNumber)
                    .ThenBy(m => m.MatchOrder)
                    .ToList() ?? new List<MatchEntity>();

                var matchById = groupMatches
                    .Where(m => m.Id.HasValue)
                    .ToDictionary(m => m.Id!.Value, m => m);

                // For team tournaments show one card per team match (Team vs Team). TeamMatchEntity
                // has no group column, so a match belongs to this group when its participants do.
                // Skip matches that touch an orphan participant (TeamId & UserId both null) — those
                // are leftover placeholder slots that would render as "Unknown vs X" otherwise.
                var groupMatchDtos = isTeamGroup
                    ? stage.TeamMatches!
                        .Where(tm => tm.HomeTeamParticipant != null
                            && tm.HomeTeamParticipant.TournamentGroupId == group.Id
                            && !IsOrphanParticipant(tm.HomeTeamParticipant)
                            && (tm.AwayTeamParticipant == null || !IsOrphanParticipant(tm.AwayTeamParticipant)))
                        .OrderBy(tm => tm.RoundNumber)
                        .ThenBy(tm => tm.MatchOrder)
                        .Select(MapGroupTeamMatchToDto)
                        .ToList()
                    : groupMatches.Select(m => MapMatchToDto(m, currentUserId, isPrivileged, matchById)).ToList();

                var dto = new GroupDto
                {
                    GroupId = group.Id!.Value,
                    Name = group.Name,
                    Matches = groupMatchDtos,
                    RoundDeadlines = groupMatches
                        .GroupBy(m => m.RoundNumber ?? 1)
                        .OrderBy(g => g.Key)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Max(m => m.RoundDeadline))
                };

                var participants = participantsByGroup.TryGetValue(group.Id!.Value, out var p) ? p : new List<TournamentParticipantEntity>();

                // Swiss standings: rank with Buchholz (sum-of-opponents'-points) as the primary
                // tiebreaker, then expose it on each row so the UI can render it next to the table.
                Dictionary<Guid, int>? opponentPointsSum = stage.Type == StageType.Swiss
                    ? ComputeSwissOpponentPointsSum(participants, groupMatches)
                    : null;

                // H2H tiebreaker source: team groups use the team-vs-team results, solo/Swiss the
                // per-match scores. Keeps display order identical to the qualification order computed
                // in CheckAndAdvanceGroupStage.
                var h2hGames = isTeamGroup
                    ? BuildTeamH2HGames(stage.TeamMatches!.Where(tm =>
                        tm.HomeTeamParticipant != null && tm.HomeTeamParticipant.TournamentGroupId == group.Id))
                    : BuildSoloH2HGames(groupMatches);

                dto.Standings = BuildGroupStandings(participants, h2hGames, opponentPointsSum);
                groupDtos.Add(dto);
            }
            return groupDtos;
        }

        private MatchStructureDto MapMatchToDto(MatchEntity m, Guid currentUserId, bool isPrivileged, Dictionary<Guid, MatchEntity> matchById)
        {
            bool canRevert = false;
            // Byes (one-sided completions) are excluded — there is no result to revert.
            if (m.Status == MatchStatus.Completed
                && (m.TeamMatchId.HasValue || (m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue)))
            {
                bool isParticipant = m.TeamMatchId.HasValue
                    ? m.HomeUserId == currentUserId || m.AwayUserId == currentUserId
                    : m.HomeParticipant?.UserId == currentUserId || m.AwayParticipant?.UserId == currentUserId;

                // "Pending" here is the broader sense — anything not yet Live/Completed/NoShow,
                // which covers both Pending (no participants assigned) and Scheduled (participants
                // agreed on a time but haven't played). See IsDownstreamUnplayed / the server gate.
                bool isNextMatchPending = !m.NextMatchId.HasValue ||
                    (matchById.TryGetValue(m.NextMatchId.Value, out var nextMatch) && IsDownstreamUnplayed(nextMatch.Status));

                // Loser-side downstream lock — third-place play-off for single-elim, LB drop
                // for DE. Mirrors the server-side guard in UpdateMatchResult so the UI doesn't
                // offer a revert that will be rejected. Loser-bracket targets may live in a
                // different stage (LB stage for DE), so a missing entry in this stage's map
                // means the lookup just couldn't see it — treat as pending (cross-stage).
                bool isLoserBracketPending = !m.NextMatchLoserBracketId.HasValue
                    || !matchById.TryGetValue(m.NextMatchLoserBracketId.Value, out var lbMatch)
                    || IsDownstreamUnplayed(lbMatch.Status);

                canRevert = isNextMatchPending && isLoserBracketPending && (isPrivileged || isParticipant);
            }

            return new MatchStructureDto
            {
                Id = m.Id!.Value,
                Round = m.RoundNumber ?? 1,
                Order = m.MatchOrder ?? 0,
                Stage = m.Stage,
                Status = m.Status,
                StartTime = m.ScheduledStartTime,
                RoundDeadline = m.RoundDeadline,
                NextMatchId = m.NextMatchId,
                NextMatchLoserBracketId = m.NextMatchLoserBracketId,
                IsUpperBracket = m.IsUpperBracket,
                IsRoundLocked = m.RoundOpenAt.HasValue && m.RoundOpenAt.Value > DateTime.UtcNow,
                MatchOpensAt = m.RoundOpenAt,
                CanRevert = canRevert,
                Evidences = m.MatchEvidences?.Select(x => x.Url!).ToList() ?? [],
                ProposedHomeScore = m.ProposedHomeScore,
                ProposedAwayScore = m.ProposedAwayScore,
                ProposedByUserId = m.ProposedByUserId,
                Home = m.HomeParticipant == null ? null : new MatchParticipantDto
                {
                    ParticipantId = m.HomeParticipant.Id!.Value,
                    UserId = m.HomeParticipant.UserId ?? Guid.Empty,
                    Username = m.HomeParticipant.User?.Username ?? m.HomeParticipant.Team?.TeamName ?? "Unknown",
                    Score = m.HomeUserScore,
                    Seed = m.HomeParticipant.Seed,
                    IsWinner = m.WinnerParticipantId == m.HomeParticipant.Id,
                    TeamName = m.HomeParticipant.Team?.TeamName
                },
                Away = m.AwayParticipant == null ? null : new MatchParticipantDto
                {
                    ParticipantId = m.AwayParticipant.Id!.Value,
                    UserId = m.AwayParticipant.UserId ?? Guid.Empty,
                    Username = m.AwayParticipant.User?.Username ?? m.AwayParticipant.Team?.TeamName ?? "Unknown",
                    Score = m.AwayUserScore,
                    Seed = m.AwayParticipant.Seed,
                    IsWinner = m.WinnerParticipantId == m.AwayParticipant.Id,
                    TeamName = m.AwayParticipant.Team?.TeamName
                }
            };
        }

        private static void PatchCanRevert(TournamentStructureDto structure, Guid currentUserId, bool isPrivileged)
        {
            if (currentUserId == Guid.Empty) return;

            var allMatches = structure.Stages
                .SelectMany(s => s.Rounds ?? Enumerable.Empty<BracketRoundDto>())
                .SelectMany(r => r.Matches)
                .Concat(structure.Stages
                    .SelectMany(s => s.Groups ?? Enumerable.Empty<GroupDto>())
                    .SelectMany(g => g.Matches))
                .ToList();

            var matchById = allMatches.ToDictionary(m => m.Id, m => m);

            // Group results feed the knockout draw via standings; once the bracket has matches,
            // reverting a group result would desync the seeding. Hide revert on group matches so the
            // UI matches the server-side lock (GetDownstreamLockReasonAsync / GetCanRevert).
            bool groupKnockoutDrawn =
                structure.Stages.Any(s => s.Type == StageType.GroupStage)
                && structure.Stages.Any(s =>
                    (s.Type == StageType.SingleEliminationBracket || s.Type == StageType.DoubleEliminationWinnersBracket)
                    && (s.Rounds?.Any(r => r.Matches != null && r.Matches.Count > 0) ?? false));

            foreach (var match in allMatches)
            {
                if (match.Status != MatchStatus.Completed)
                {
                    match.CanRevert = false;
                    continue;
                }

                // A completed match missing a side is a bye (Swiss free win or an elimination
                // walkover) — there is no result to revert.
                if (match.Home == null || match.Away == null)
                {
                    match.CanRevert = false;
                    continue;
                }

                // Group result with the knockout already drawn → locked (see groupKnockoutDrawn above).
                if (groupKnockoutDrawn && match.Stage == MatchStage.GroupStage)
                {
                    match.CanRevert = false;
                    continue;
                }

                bool isParticipant = match.Home?.UserId == currentUserId || match.Away?.UserId == currentUserId;

                bool downstreamPending;
                if (match.TeamMatchId.HasValue)
                {
                    // Team matches link forward via NextTeamMatchId (next round) and
                    // NextTeamMatchLoserBracketId (third-place play-off) instead of NextMatchId.
                    bool nextPending = match.NextTeamMatchId == null ||
                        (matchById.TryGetValue(match.NextTeamMatchId.Value, out var nextTm) && nextTm.Status == MatchStatus.Pending);
                    bool thirdPlacePending = match.NextTeamMatchLoserBracketId == null ||
                        (matchById.TryGetValue(match.NextTeamMatchLoserBracketId.Value, out var thirdTm) && thirdTm.Status == MatchStatus.Pending);
                    downstreamPending = nextPending && thirdPlacePending;
                }
                else
                {
                    // Downstream counts as "unplayed" while Pending or Scheduled — Scheduled just
                    // means players agreed on a time, not that the match was actually played, so the
                    // server (GetDownstreamLockReasonAsync) treats it the same and we mirror that here.
                    bool nextPending = match.NextMatchId == null ||
                        (matchById.TryGetValue(match.NextMatchId.Value, out var nextM) && IsDownstreamUnplayed(nextM.Status));
                    // Loser-side downstream — third-place play-off (single-elim) or LB drop (DE).
                    // A miss in matchById means the target is in another stage we couldn't see
                    // from this match's vantage; treat as pending and let the server enforce.
                    bool loserDownstreamPending = match.NextMatchLoserBracketId == null
                        || !matchById.TryGetValue(match.NextMatchLoserBracketId.Value, out var lbM)
                        || IsDownstreamUnplayed(lbM.Status);
                    downstreamPending = nextPending && loserDownstreamPending;
                }

                // In approval-required tournaments, only privileged users (admin / hub owner / hub admin)
                // can revert a confirmed result. Participants must contact an admin to dispute it,
                // otherwise the approval gate could be bypassed via the edit flow.
                bool participantCanRevert = isParticipant && !structure.RequireResultApproval;
                match.CanRevert = downstreamPending && (isPrivileged || participantCanRevert);
            }
        }

        // A TournamentParticipant row with neither a team nor a user attached — orphan placeholder
        // left over from team-tournament setups where the team was deleted or the slot was never
        // assigned. Filtered out of standings & matches so it doesn't render as "Unknown" / GUID.
        private static bool IsOrphanParticipant(TournamentParticipantEntity p)
            => p.TeamId == null && p.UserId == null;

        // Pass `opponentPointsSum` (Buchholz) for Swiss; the chain becomes
        // points → Buchholz → GD → H2H → GF → name. Non-Swiss callers pass null, falling
        // back to (points → GD → H2H → GF → name) — Buchholz is not meaningful in
        // round-robin groups where everyone faces everyone.
        private static List<LeagueStandingDto> BuildGroupStandings(
            List<TournamentParticipantEntity> participants,
            IReadOnlyList<H2HGame> h2hGames,
            Dictionary<Guid, int>? opponentPointsSum = null)
        {
            var standings = participants
                .Where(p => !IsOrphanParticipant(p))
                .Select(p => new LeagueStandingDto
                {
                    ParticipantId = p.Id ?? Guid.Empty,
                    // Team participants have no UserId — fall back to empty so standings don't crash.
                    // The client treats an empty UserId as "not a player" and disables the row tap.
                    UserId = p.UserId ?? Guid.Empty,
                    Name = p.Team?.TeamName ?? p.User?.Username ?? p.UserId?.ToString() ?? "",
                    Points = p.Points,
                    Wins = p.Wins,
                    Draws = p.Draws,
                    Losses = p.Losses,
                    GoalsFor = p.GoalsFor,
                    GoalsAgainst = p.GoalsAgainst,
                    GoalDifference = p.GoalsFor - p.GoalsAgainst,
                    MatchesPlayed = p.Wins + p.Draws + p.Losses,
                    OpponentPointsSum = opponentPointsSum != null && p.Id.HasValue
                        ? opponentPointsSum.GetValueOrDefault(p.Id.Value)
                        : (int?)null
                })
                .OrderByDescending(s => s.Points)
                .ThenByDescending(s => s.OpponentPointsSum ?? 0)
                .ThenByDescending(s => s.GoalDifference)
                .ThenByDescending(s => s.GoalsFor)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ResolveHeadToHeadInTiedChunks(
                standings,
                s => s.ParticipantId,
                s => (s.Points, s.OpponentPointsSum ?? 0, s.GoalDifference),
                h2hGames);

            for (int i = 0; i < standings.Count; i++) standings[i].Position = i + 1;
            return standings;
        }

        // One head-to-head game between two tied participants. Winner == null means a draw (each
        // gets 1 h2h point); HomeScore/AwayScore feed the h2h GD/GF refinement.
        private readonly record struct H2HGame(Guid Home, Guid Away, int HomeScore, int AwayScore, Guid? Winner);

        // Solo group/league matches → h2h games. Byes and unscored/unfinished matches are dropped.
        private static List<H2HGame> BuildSoloH2HGames(IEnumerable<MatchEntity> matches) =>
            matches
                .Where(m => !m.TeamMatchId.HasValue
                    && m.Status == MatchStatus.Completed
                    && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue
                    && m.HomeUserScore.HasValue && m.AwayUserScore.HasValue)
                .Select(m =>
                {
                    int hs = m.HomeUserScore!.Value, aw = m.AwayUserScore!.Value;
                    Guid? winner = hs > aw ? m.HomeParticipantId : aw > hs ? m.AwayParticipantId : null;
                    return new H2HGame(m.HomeParticipantId!.Value, m.AwayParticipantId!.Value, hs, aw, winner);
                })
                .ToList();

        // Team group matches → h2h games. Points come from the team-match winner (3/1/0, matching the
        // standings), GD/GF from the aggregated sub-match scores (the same totals the standings use).
        private static List<H2HGame> BuildTeamH2HGames(IEnumerable<TeamMatchEntity> teamMatches) =>
            teamMatches
                .Where(tm => tm.Status == TeamMatchStatus.Completed
                    && tm.HomeTeamParticipantId.HasValue && tm.AwayTeamParticipantId.HasValue)
                .Select(tm =>
                {
                    int home = 0, away = 0;
                    if (tm.SubMatches != null)
                        foreach (var sm in tm.SubMatches)
                        {
                            home += sm.HomeUserScore ?? 0;
                            away += sm.AwayUserScore ?? 0;
                        }
                    return new H2HGame(tm.HomeTeamParticipantId!.Value, tm.AwayTeamParticipantId!.Value, home, away, tm.WinnerTeamParticipantId);
                })
                .ToList();

        // Head-to-head tiebreaker applied within chunks of rows tied on (Points, OPS, GD).
        // For each tied subset, builds a mini-table from the games played *between* exactly those
        // participants and re-orders by (h2h points → h2h GD → h2h GF). Rows still tied after the
        // mini-table preserve their inbound order, so the outer chain's remaining tiebreakers
        // (GF → name) keep acting as the final word. Solo and team groups both feed in via H2HGame.
        private static void ResolveHeadToHeadInTiedChunks<T>(
            List<T> ordered,
            Func<T, Guid> participantIdOf,
            Func<T, (int Points, int Ops, int Gd)> tieKeyOf,
            IReadOnlyList<H2HGame> games)
        {
            if (ordered.Count < 2 || games.Count == 0) return;

            int i = 0;
            while (i < ordered.Count)
            {
                var key = tieKeyOf(ordered[i]);
                int j = i + 1;
                while (j < ordered.Count && tieKeyOf(ordered[j]).Equals(key)) j++;

                int chunkSize = j - i;
                if (chunkSize > 1)
                {
                    var tiedIds = new HashSet<Guid>();
                    for (int k = i; k < j; k++) tiedIds.Add(participantIdOf(ordered[k]));

                    var h2hPts = new Dictionary<Guid, int>(chunkSize);
                    var h2hGd = new Dictionary<Guid, int>(chunkSize);
                    var h2hGf = new Dictionary<Guid, int>(chunkSize);
                    foreach (var id in tiedIds) { h2hPts[id] = 0; h2hGd[id] = 0; h2hGf[id] = 0; }

                    foreach (var g in games)
                    {
                        if (!tiedIds.Contains(g.Home) || !tiedIds.Contains(g.Away)) continue;

                        h2hGf[g.Home] += g.HomeScore; h2hGf[g.Away] += g.AwayScore;
                        h2hGd[g.Home] += g.HomeScore - g.AwayScore; h2hGd[g.Away] += g.AwayScore - g.HomeScore;

                        if (g.Winner == g.Home) h2hPts[g.Home] += 3;
                        else if (g.Winner == g.Away) h2hPts[g.Away] += 3;
                        else { h2hPts[g.Home] += 1; h2hPts[g.Away] += 1; }
                    }

                    // Stable sort: rows tied on all three h2h criteria keep incoming order
                    // so the outer GF → name fallback applies as last resort.
                    var withIdx = new (T Item, int Idx)[chunkSize];
                    for (int k = 0; k < chunkSize; k++) withIdx[k] = (ordered[i + k], k);
                    Array.Sort(withIdx, (a, b) =>
                    {
                        var ida = participantIdOf(a.Item);
                        var idb = participantIdOf(b.Item);
                        int cmp = h2hPts[idb].CompareTo(h2hPts[ida]);
                        if (cmp != 0) return cmp;
                        cmp = h2hGd[idb].CompareTo(h2hGd[ida]);
                        if (cmp != 0) return cmp;
                        cmp = h2hGf[idb].CompareTo(h2hGf[ida]);
                        if (cmp != 0) return cmp;
                        return a.Idx.CompareTo(b.Idx);
                    });
                    for (int k = 0; k < chunkSize; k++) ordered[i + k] = withIdx[k].Item;
                }

                i = j;
            }
        }

        // F109: participant ids + push tokens are resolved here (awaited, while the request-scoped
        // DbContext is alive); only the push send is fired-and-forgotten. The old version queried
        // this.AppUnitOfWork inside Task.Run, racing against the disposed request-scoped context.
        private async Task SendNotification(TournamentEntity tournament, Guid tournamentId)
        {
            var userIds = await this.AppUnitOfWork.TournamentParticipantRepository.GetAllUserIdsByTournamentId(tournamentId);
            if (userIds.Count == 0) return;

            var pushTokens = await this.AppUnitOfWork.UserRepository.GetPushTokensByUserIds(userIds);
            if (pushTokens.Count == 0) return;

            var title = tournament.Name;

            _ = Task.Run(async () =>
            {
                try
                {
                    await notificationService.SendToManyAsync(
                        pushTokens,
                        $"{title}",
                        $"Tournament is now live. Good luck!",
                        new { tournamentId });
                }
                catch { /* fire-and-forget */ }
            });
        }

        #endregion 5. Private Helpers (Core Logic)
    }
}