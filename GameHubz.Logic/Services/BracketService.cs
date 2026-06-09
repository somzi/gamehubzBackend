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

        public BracketService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            HubActivityService hubActivityService,
            ICacheService cacheService,
            INotificationService notificationService,
            TournamentAuthorizationService tournamentAuth)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.hubActivityService = hubActivityService;
            this.cacheService = cacheService;
            this.notificationService = notificationService;
            this.tournamentAuth = tournamentAuth;
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

        private async Task<TournamentStructureDto> BuildStructureResponse(
            TournamentEntity tournament, Guid currentUserId, bool isPrivileged, string cacheKey)
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
                    || stageEntity.Type == StageType.DoubleEliminationLosersBracket)
                {
                    // LB matches that the DE generator collapsed (no participants, no winner —
                    // both upstream feeders were byes) get filtered out of the structure so the
                    // UI doesn't render empty "TBD vs TBD" placeholders for bypassed rounds.
                    bool dropCollapsedByes = stageEntity.Type == StageType.DoubleEliminationLosersBracket;
                    stageDto.Rounds = tournament.IsTeamTournament
                        ? MapTeamBracketRounds(stageEntity.TeamMatches)
                        : MapBracketRounds(stageEntity.Matches, currentUserId, isPrivileged, dropCollapsedByes);
                }
                else if (stageEntity.Type == StageType.GroupStage || stageEntity.Type == StageType.League)
                {
                    stageDto.Groups = await MapGroups(stageEntity, currentUserId, isPrivileged);
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
                        await GenerateTeamLeagueTournament(tournamentId, doubleRoundRobin: false, roundDuration: roundDuration);
                    else
                        await GenerateLeagueTournament(tournamentId, doubleRoundRobin: false, roundDuration: roundDuration);
                    break;

                case TournamentFormat.DoubleElimination:
                    await GenerateDoubleEliminationBracket(tournamentId);
                    break;

                case TournamentFormat.GroupStageWithKnockout:
                    if (!tournament.GroupsCount.HasValue || !tournament.QualifiersPerGroup.HasValue)
                        throw new Exception("Group count and qualifiers count are required for this format.");
                    if (tournament.IsTeamTournament)
                        await GenerateTeamGroupStageWithKnockout(tournamentId, tournament.GroupsCount.Value, tournament.QualifiersPerGroup!.Value, roundDuration);
                    else
                        await GenerateGroupStageWithKnockout(tournamentId, tournament.GroupsCount.Value, tournament.QualifiersPerGroup!.Value, roundDuration);
                    break;

                default:
                    throw new Exception($"Tournament format {tournament.Format} not supported");
            }

            await cacheService.RemoveAsync($"tournament:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
            await cacheService.RemoveAsync($"league_standings:{tournamentId}");
            await cacheService.RemoveAsync($"pdf:bracket:{tournamentId}");
            await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentLive);

            // Notify all participants that the tournament is now live
            SendNotification(tournament, tournamentId);
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

            tournament.Status = TournamentStatus.InProgress;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
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

            tournament.Status = TournamentStatus.InProgress;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            await this.SaveAsync();
        }

        public async Task GenerateGroupStageWithKnockout(Guid tournamentId, int numberOfGroups, int qualifiersPerGroup, TimeSpan? roundDuration = null)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);
            var participants = tournament!.TournamentParticipants?.ToList();

            if (participants!.Count < numberOfGroups * 2)
                throw new Exception($"Not enough participants. Need at least {numberOfGroups * 2} players for {numberOfGroups} groups.");

            int totalQualifiers = numberOfGroups * qualifiersPerGroup;
            if (!IsPowerOfTwo(totalQualifiers))
                throw new Exception($"Total qualifiers ({totalQualifiers}) must be a power of 2 (4, 8, 16, 32) for the bracket to work.");

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
                    false
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

            var knockoutStage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.SingleEliminationBracket,
                Order = 2,
                Name = "Knockout Stage",
                QualifiedPlayersCount = totalQualifiers
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(knockoutStage, this.UserContextReader);

            tournament.Status = TournamentStatus.InProgress;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
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

            tournament.Status = TournamentStatus.InProgress;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            await this.SaveAsync();
        }

        public async Task GenerateTeamGroupStageWithKnockout(Guid tournamentId, int numberOfGroups, int qualifiersPerGroup, TimeSpan? roundDuration = null)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);

            if (!tournament!.TeamSize.HasValue)
                throw new Exception("TeamSize is required for team tournaments.");

            int teamSize = tournament.TeamSize.Value;

            var participants = tournament.TournamentParticipants?.ToList();

            if (participants!.Count < numberOfGroups * 2)
                throw new Exception($"Not enough participants. Need at least {numberOfGroups * 2} teams for {numberOfGroups} groups.");

            int totalQualifiers = numberOfGroups * qualifiersPerGroup;
            if (!IsPowerOfTwo(totalQualifiers))
                throw new Exception($"Total qualifiers ({totalQualifiers}) must be a power of 2 (4, 8, 16, 32) for the bracket to work.");

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

                var tms = GenerateRoundRobinTeamMatches(tournamentId, groupStage.Id.Value, ps, false);
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

            var knockoutStage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.SingleEliminationBracket,
                Order = 2,
                Name = "Knockout Stage",
                QualifiedPlayersCount = totalQualifiers
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(knockoutStage, this.UserContextReader);

            tournament.Status = TournamentStatus.InProgress;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            await this.SaveAsync();
        }

        public async Task GenerateDoubleEliminationBracket(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);
            var participants = tournament!.TournamentParticipants?.ToList();

            if (participants == null || participants.Count < 4)
                throw new Exception("Double elimination requires at least 4 participants.");

            // Team double-elimination is not yet wired up — the LB drop-in routing and slot
            // overrides only exist on MatchEntity, not TeamMatchEntity. Surface a clear error
            // instead of silently producing a half-built bracket.
            if (tournament.IsTeamTournament)
                throw new Exception("Team double-elimination tournaments are not supported yet.");

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

            tournament.Status = TournamentStatus.InProgress;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
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

            tournament.Status = TournamentStatus.InProgress;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
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

            if (match == null) throw new Exception("Match not found");
            if (match.TournamentId != request.TournamentId) throw new Exception("Match wrong tournament");
            if (match.RoundOpenAt.HasValue && match.RoundOpenAt.Value > DateTime.UtcNow)
                throw new Exception("This round is not open yet.");

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
                ?? throw new Exception("Tournament not found");
            bool isPrivileged = await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, currentUser);

            // When the tournament requires result approval and the caller is a participant
            // (not a tournament manager — platform admin, hub owner, or hub admin),
            // persist a proposal instead of completing the match.
            if (approvalCtx.RequireResultApproval && !isPrivileged)
            {
                if (!IsMatchParticipant(match, currentUser.UserId))
                    throw new Exception("You are not a participant of this match.");

                // Once a result is confirmed in approval mode, only an admin / hub owner can change it.
                // Otherwise the approval gate could be bypassed via the edit path.
                if (match.Status == MatchStatus.Completed)
                    throw new Exception("This result is final. Ask the hub owner or an admin to amend it.");

                await SaveProposal(match, request.HomeScore, request.AwayScore, currentUser);
                return;
            }

            // 1. REVERT LOGIC
            if (match.Status == MatchStatus.Completed)
            {
                if (match.TeamMatchId.HasValue)
                {
                    // Team sub-matches don't carry the Next* links (those live on TeamMatchEntity), so the
                    // solo guards below never fire for them. Mirror the lock at the team-match level, otherwise
                    // reverting a semi-final would silently cascade-delete an already-played final / third-place.
                    var parentTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(match.TeamMatchId.Value);

                    if (parentTeamMatch.NextTeamMatchId.HasValue)
                    {
                        var nextTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(parentTeamMatch.NextTeamMatchId.Value);
                        if (nextTeamMatch.Status != TeamMatchStatus.Pending)
                            throw new Exception("This match is locked because the next round has already progressed. To edit this, you must revert the downstream match first.");
                    }

                    if (parentTeamMatch.NextTeamMatchLoserBracketId.HasValue)
                    {
                        var thirdPlaceTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(parentTeamMatch.NextTeamMatchLoserBracketId.Value);
                        if (thirdPlaceTeamMatch.Status != TeamMatchStatus.Pending)
                            throw new Exception("This match is locked because the third-place match has already progressed. To edit this, you must revert the third-place match first.");
                    }
                }
                else
                {
                    if (nextMatch != null && nextMatch.Status != MatchStatus.Pending)
                    {
                        throw new Exception("This match is locked because the next round has already progressed. To edit this, you must revert the downstream match first.");
                    }

                    if (loserBracketMatch != null && loserBracketMatch.Status != MatchStatus.Pending)
                    {
                        // Single-elim → third-place play-off. DE → the Losers Bracket match that
                        // received this WB match's loser. Same lock applies in both cases.
                        bool isThirdPlace = loserBracketMatch.Stage == MatchStage.ThirdPlace;
                        var label = isThirdPlace ? "third-place match" : "loser bracket match";
                        throw new Exception($"This match is locked because the {label} has already progressed. To edit this, you must revert the downstream {label} first.");
                    }
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
        /// Approves the pending proposal stored on the match: commits the proposed scores,
        /// clears proposal fields, advances the bracket. Caller must be the opposing participant
        /// or an admin / hub owner.
        /// </summary>
        public async Task ApproveProposedResult(Guid matchId)
        {
            var match = await this.AppUnitOfWork.MatchRepository.GetWithStage(matchId);
            if (match == null) throw new Exception("Match not found");

            if (match.ProposedByUserId == null || match.ProposedHomeScore == null || match.ProposedAwayScore == null)
                throw new Exception("No pending result to approve for this match.");

            if (match.Status == MatchStatus.Completed)
                throw new Exception("This match is already completed.");

            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            bool isPrivileged = await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, currentUser);

            if (!isPrivileged)
            {
                if (!IsMatchParticipant(match, currentUser.UserId))
                    throw new Exception("You are not a participant of this match.");

                // The proposer cannot also be the approver — the opponent (or a tournament manager) confirms.
                if (match.ProposedByUserId == currentUser.UserId)
                    throw new Exception("Your opponent must approve the result you reported.");
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
            if (match == null) throw new Exception("Match not found");

            if (match.ProposedByUserId == null)
                throw new Exception("No pending result to reject for this match.");

            if (match.Status == MatchStatus.Completed)
                throw new Exception("This match is already completed.");

            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            bool isPrivileged = await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, currentUser);

            if (!isPrivileged)
            {
                var fullMatch = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
                if (fullMatch == null || !IsMatchParticipant(fullMatch, currentUser.UserId))
                    throw new Exception("You are not a participant of this match.");

                if (match.ProposedByUserId == currentUser.UserId)
                    throw new Exception("You can't reject your own proposal — submit a corrected result instead.");
            }

            match.ProposedHomeScore = null;
            match.ProposedAwayScore = null;
            match.ProposedByUserId = null;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();
            await cacheService.RemoveAsync($"bracket:{match.TournamentId}");
            await cacheService.RemoveAsync($"league_standings:{match.TournamentId}");
        }

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

            // 5. Apply Rules & Advance
            if (match.TournamentStage?.Type == StageType.League || match.TournamentStage?.Type == StageType.GroupStage)
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
                }
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

            await cacheService.RemoveAsync($"bracket:{tournamentId}");
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
            await cacheService.RemoveAsync($"league_standings:{match.TournamentId}");
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
                    UserId = p.UserId!.Value,
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
            Guid awayId,
            int homeScore,
            int awayScore,
            Guid? winnerId)
        {
            if (!byId.TryGetValue(homeId, out var home)) return;
            if (!byId.TryGetValue(awayId, out var away)) return;

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

            if (teamMatch.NextTeamMatchId.HasValue && oldWinner.HasValue)
            {
                var nextTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(teamMatch.NextTeamMatchId.Value);
                if (nextTeamMatch != null)
                {
                    bool isHomeSlot = (teamMatch.MatchOrder % 2) == 0;
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

            // Reverting a semi-final must also pull its loser back out of the third-place play-off.
            if (teamMatch.NextTeamMatchLoserBracketId.HasValue && oldWinner.HasValue)
            {
                var loserId = oldWinner.Value == teamMatch.HomeTeamParticipantId
                    ? teamMatch.AwayTeamParticipantId
                    : teamMatch.HomeTeamParticipantId;

                var thirdPlaceMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(teamMatch.NextTeamMatchLoserBracketId.Value);
                if (thirdPlaceMatch != null && loserId.HasValue)
                {
                    bool loserIsHomeSlot = (teamMatch.MatchOrder % 2) == 0;
                    if (loserIsHomeSlot)
                        thirdPlaceMatch.HomeTeamParticipantId = null;
                    else
                        thirdPlaceMatch.AwayTeamParticipantId = null;

                    foreach (var sm in thirdPlaceMatch.SubMatches)
                        await this.AppUnitOfWork.MatchRepository.HardDeleteEntity(sm);

                    if (thirdPlaceMatch.Status == TeamMatchStatus.Completed)
                    {
                        thirdPlaceMatch.WinnerTeamParticipantId = null;
                        thirdPlaceMatch.Status = TeamMatchStatus.Pending;
                    }

                    await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(thirdPlaceMatch, this.UserContextReader);
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
            if (nextMatch != null)
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
            else if (match.Stage == MatchStage.GrandFinal)
            {
                // DE terminal: the Grand Final winner is the tournament champion.
                var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(match.TournamentId);
                tournament.WinnerUserId = winnerUserId;
                tournament.Status = TournamentStatus.Completed;
                await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
                await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
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

                    tournament.WinnerUserId = finalMatch!.Id == match.Id
                        ? winnerUserId
                        : await ResolveParticipantUserId(finalMatch.WinnerParticipantId);
                    tournament.Status = TournamentStatus.Completed;
                    await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);

                    await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
                }
            }
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

            tournament.Status = TournamentStatus.Completed;

            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
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
                await cacheService.RemoveAsync($"league_standings:{teamMatch.TournamentId}");
                await cacheService.RemoveAsync($"pdf:bracket:{teamMatch.TournamentId}");
                await cacheService.RemoveAsync($"tournament:{teamMatch.TournamentId}");
                return;
            }

            // Winner determined
            teamMatch.WinnerTeamParticipantId = winnerTeamParticipantId;
            teamMatch.Status = TeamMatchStatus.Completed;
            await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(teamMatch, this.UserContextReader);

            // Route the semi-final loser into the third-place play-off (if configured).
            if (teamMatch.NextTeamMatchLoserBracketId.HasValue)
            {
                var loserTeamParticipantId = winnerTeamParticipantId == teamMatch.HomeTeamParticipantId
                    ? teamMatch.AwayTeamParticipantId
                    : teamMatch.HomeTeamParticipantId;

                if (loserTeamParticipantId.HasValue)
                {
                    var thirdPlaceMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(teamMatch.NextTeamMatchLoserBracketId.Value);

                    bool loserIsHomeSlot = (teamMatch.MatchOrder % 2) == 0;
                    if (loserIsHomeSlot)
                        thirdPlaceMatch.HomeTeamParticipantId = loserTeamParticipantId;
                    else
                        thirdPlaceMatch.AwayTeamParticipantId = loserTeamParticipantId;

                    await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(thirdPlaceMatch, this.UserContextReader);

                    // Once both losers are in, create the play-off's sub-matches.
                    if (thirdPlaceMatch.HomeTeamParticipantId.HasValue && thirdPlaceMatch.AwayTeamParticipantId.HasValue)
                        await CreateSubMatchesForTeamMatch(thirdPlaceMatch);
                }
            }

            // Advance or complete
            if (teamMatch.NextTeamMatchId.HasValue)
            {
                var nextTeamMatch = await this.AppUnitOfWork.TeamMatchRepository.ShallowGetByIdOrThrowIfNull(teamMatch.NextTeamMatchId.Value);

                bool isHomeSlot = (teamMatch.MatchOrder % 2) == 0;
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

                    tournament.Status = TournamentStatus.Completed;
                    await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
                    await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
                }
            }

            await this.SaveAsync();

            await cacheService.RemoveAsync($"bracket:{teamMatch.TournamentId}");
            await cacheService.RemoveAsync($"league_standings:{teamMatch.TournamentId}");
            await cacheService.RemoveAsync($"pdf:bracket:{teamMatch.TournamentId}");
            await cacheService.RemoveAsync($"tournament:{teamMatch.TournamentId}");
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

            if (knockoutStage == null || knockoutStage.Type != StageType.SingleEliminationBracket) return;

            bool hasMatches = await this.AppUnitOfWork.MatchRepository.HasMatchesForStage(knockoutStage.Id!.Value);
            if (hasMatches) return;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);

            // For team tournaments, also require all team matches to be finalized
            if (tournament.IsTeamTournament)
            {
                var groupTeamMatches = await this.AppUnitOfWork.TeamMatchRepository.GetByStageId(groupStageId);
                if (groupTeamMatches.Any(tm => tm.Status != TeamMatchStatus.Completed))
                    return;
            }

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

                for (int rank = 0; rank < Math.Min(qualifiersPerGroup, sorted.Count); rank++)
                {
                    qualifiers.Add((sorted[rank], rank + 1, group.Name));
                }
            }

            if (qualifiers.Count < 2) throw new Exception("Not enough qualifiers to create knockout bracket.");

            int totalQualifiers = qualifiers.Count;
            var rand = new Random();

            var pots = qualifiers
                .GroupBy(q => q.groupRank)
                .OrderBy(g => g.Key)
                .Select(g => g.OrderBy(_ => rand.Next()).ToList())
                .ToList();

            bool drawSuccessful = false;
            List<(TournamentParticipantEntity p1, TournamentParticipantEntity p2)> drawnPairs = null!;

            int maxAttempts = 100;
            while (!drawSuccessful && maxAttempts > 0)
            {
                maxAttempts--;
                drawSuccessful = true;
                drawnPairs = new List<(TournamentParticipantEntity, TournamentParticipantEntity)>();

                var potsCopy = pots.Select(p => p.ToList()).ToList();
                int left = 0, right = potsCopy.Count - 1;

                while (left <= right)
                {
                    if (left < right)
                    {
                        var potA = potsCopy[left].OrderBy(_ => rand.Next()).ToList();
                        var potB = potsCopy[right];

                        foreach (var first in potA)
                        {
                            var validSecond = potB
                                .Where(s => s.groupName != first.groupName)
                                .OrderBy(_ => rand.Next())
                                .FirstOrDefault();

                            if (validSecond == default)
                            {
                                drawSuccessful = false;
                                break;
                            }

                            drawnPairs.Add((first.participant, validSecond.participant));
                            potB.Remove(validSecond);
                        }
                    }
                    else
                    {
                        // N=1 ili srednji pot (N=3) — uparujemo iz istog pota
                        var middlePot = potsCopy[left].OrderBy(_ => rand.Next()).ToList();

                        while (middlePot.Count > 0)
                        {
                            var first = middlePot.First();
                            middlePot.Remove(first);

                            var validSecond = middlePot
                                .Where(s => s.groupName != first.groupName)
                                .OrderBy(_ => rand.Next())
                                .FirstOrDefault();

                            if (validSecond == default)
                            {
                                drawSuccessful = false;
                                break;
                            }

                            drawnPairs.Add((first.participant, validSecond.participant));
                            middlePot.Remove(validSecond);
                        }
                    }

                    if (!drawSuccessful) break;
                    left++;
                    right--;
                }
            }

            if (!drawSuccessful)
                throw new Exception($"Draw failed after 100 attempts. Qualifiers: {qualifiers.Count}");

            // ------------------------------------------------------------------
            // DISTRIBUCIJA U KOSTUR: Razdvajanje istih grupa na suprotne strane
            // ------------------------------------------------------------------
            int numPairs = drawnPairs.Count;
            var pairSeeds = GetStandardSeedOrder(GetNextPowerOfTwo(numPairs));
            var validPairSeeds = pairSeeds.Where(s => s <= numPairs).ToList();

            var topHalfSeeds = validPairSeeds.Take(numPairs / 2 + numPairs % 2).ToList();
            var bottomHalfSeeds = validPairSeeds.Skip(numPairs / 2 + numPairs % 2).ToList();

            int bestScore = int.MaxValue;
            List<(TournamentParticipantEntity p1, TournamentParticipantEntity p2)> bestDistribution = null!;

            for (int attempt = 0; attempt < 500; attempt++)
            {
                var shuffledPairs = drawnPairs.OrderBy(_ => rand.Next()).ToList();
                var topPairs = shuffledPairs.Take(topHalfSeeds.Count).ToList();
                var bottomPairs = shuffledPairs.Skip(topHalfSeeds.Count).ToList();

                var topGroups = topPairs.SelectMany(p => new[] { p.p1.TournamentGroupId, p.p2.TournamentGroupId }).ToList();
                var bottomGroups = bottomPairs.SelectMany(p => new[] { p.p1.TournamentGroupId, p.p2.TournamentGroupId }).ToList();

                int topDuplicates = topGroups.Count - topGroups.Distinct().Count();
                int bottomDuplicates = bottomGroups.Count - bottomGroups.Distinct().Count();
                int totalScore = topDuplicates + bottomDuplicates;

                if (totalScore < bestScore)
                {
                    bestScore = totalScore;
                    bestDistribution = shuffledPairs;
                }

                if (bestScore == 0) break;
            }

            var finalTop = bestDistribution.Take(topHalfSeeds.Count).ToList();
            var finalBottom = bestDistribution.Skip(topHalfSeeds.Count).ToList();

            var seedMap = new Dictionary<Guid, int>();

            for (int i = 0; i < finalTop.Count; i++)
            {
                int seedP1 = topHalfSeeds[i];
                int seedP2 = totalQualifiers - seedP1 + 1;
                seedMap[finalTop[i].p1.Id!.Value] = seedP1;
                seedMap[finalTop[i].p2.Id!.Value] = seedP2;
            }

            for (int i = 0; i < finalBottom.Count; i++)
            {
                int seedP1 = bottomHalfSeeds[i];
                int seedP2 = totalQualifiers - seedP1 + 1;
                seedMap[finalBottom[i].p1.Id!.Value] = seedP1;
                seedMap[finalBottom[i].p2.Id!.Value] = seedP2;
            }
            // ------------------------------------------------------------------

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

            var seededQualifiers = qualifiers
                .Select(q => q.participant)
                .OrderBy(p => p.Seed ?? 999)
                .ToList();

            var bracketSeeded = GetStandardBracketSeeding(seededQualifiers);

            if (tournament.IsTeamTournament)
            {
                int teamSize = tournament.TeamSize ?? 1;
                int playerCount = GetNextPowerOfTwo(bracketSeeded.Count);

                var bracketSlots = bracketSeeded.Cast<TournamentParticipantEntity?>().ToList();
                while (bracketSlots.Count < playerCount) bracketSlots.Add(null);

                var teamMatches = GenerateEliminationTeamMatches(tournamentId, knockoutStage.Id!.Value, bracketSlots);

                if (tournament.HasThirdPlaceMatch)
                    BuildThirdPlaceTeamMatchIfApplicable(teamMatches, (int)Math.Log2(playerCount), bracketSeeded.Count, tournamentId, knockoutStage.Id);

                foreach (var tm in teamMatches)
                    await this.AppUnitOfWork.TeamMatchRepository.AddEntity(tm, this.UserContextReader);

                var membersByParticipant = await BuildMembersByParticipantMap(bracketSeeded);

                foreach (var tm in teamMatches)
                {
                    if (!tm.HomeTeamParticipantId.HasValue || !tm.AwayTeamParticipantId.HasValue) continue;
                    if (tm.Status == TeamMatchStatus.Completed) continue;

                    var subs = BuildSubMatchesForTeamMatch(tm, teamSize, null, membersByParticipant, rand);
                    foreach (var sm in subs)
                        await this.AppUnitOfWork.MatchRepository.AddEntity(sm, this.UserContextReader);
                }
            }
            else
            {
                var matches = GenerateEliminationMatches(tournamentId, knockoutStage.Id!.Value, bracketSeeded, tournament.HasThirdPlaceMatch);

                foreach (var m in matches)
                {
                    m.Status = MatchStatus.Pending;
                    await this.AppUnitOfWork.MatchRepository.AddEntity(m, this.UserContextReader);
                }
            }

            await this.SaveAsync();
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
            return bracketOrder.Select(i => sorted[i]).ToList();
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
            || type == StageType.DoubleEliminationLosersBracket;

        private string GetGroupName(int index)
        {
            const string l = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            return index < l.Length ? l[index].ToString() : (index + 1).ToString();
        }

        private List<BracketRoundDto> MapTeamBracketRounds(List<TeamMatchEntity>? teamMatches)
        {
            var rounds = new List<BracketRoundDto>();
            if (teamMatches == null || !teamMatches.Any()) return rounds;

            // The third-place play-off shares the final's RoundNumber, so Max gives the true depth of the tree.
            int totalRounds = teamMatches.Max(m => m.RoundNumber ?? 1);

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
                Stage = tm.IsThirdPlace
                    ? MatchStage.ThirdPlace
                    : StageFromRoundsFromEnd(totalRounds - round + 1),
                Status = MapTeamMatchStatus(tm.Status),
                TeamMatchId = tm.Id,
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

        private async Task<List<GroupDto>> MapGroups(TournamentStageEntity stage, Guid currentUserId, bool isPrivileged)
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

                var dto = new GroupDto
                {
                    GroupId = group.Id!.Value,
                    Name = group.Name,
                    Matches = groupMatches.Select(m => MapMatchToDto(m, currentUserId, isPrivileged, matchById)).ToList(),
                    RoundDeadlines = groupMatches
                        .GroupBy(m => m.RoundNumber ?? 1)
                        .OrderBy(g => g.Key)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Max(m => m.RoundDeadline))
                };

                var participants = participantsByGroup.TryGetValue(group.Id!.Value, out var p) ? p : new List<TournamentParticipantEntity>();
                dto.Standings = BuildGroupStandings(participants);
                groupDtos.Add(dto);
            }
            return groupDtos;
        }

        private MatchStructureDto MapMatchToDto(MatchEntity m, Guid currentUserId, bool isPrivileged, Dictionary<Guid, MatchEntity> matchById)
        {
            bool canRevert = false;
            if (m.Status == MatchStatus.Completed)
            {
                bool isParticipant = m.TeamMatchId.HasValue
                    ? m.HomeUserId == currentUserId || m.AwayUserId == currentUserId
                    : m.HomeParticipant?.UserId == currentUserId || m.AwayParticipant?.UserId == currentUserId;

                bool isNextMatchPending = !m.NextMatchId.HasValue ||
                    (matchById.TryGetValue(m.NextMatchId.Value, out var nextMatch) && nextMatch.Status == MatchStatus.Pending);

                // Loser-side downstream lock — third-place play-off for single-elim, LB drop
                // for DE. Mirrors the server-side guard in UpdateMatchResult so the UI doesn't
                // offer a revert that will be rejected. Loser-bracket targets may live in a
                // different stage (LB stage for DE), so a missing entry in this stage's map
                // means the lookup just couldn't see it — treat as pending (cross-stage).
                bool isLoserBracketPending = !m.NextMatchLoserBracketId.HasValue
                    || !matchById.TryGetValue(m.NextMatchLoserBracketId.Value, out var lbMatch)
                    || lbMatch.Status == MatchStatus.Pending;

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

            foreach (var match in allMatches)
            {
                if (match.Status != MatchStatus.Completed)
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
                    bool nextPending = match.NextMatchId == null ||
                        (matchById.TryGetValue(match.NextMatchId.Value, out var nextM) && nextM.Status == MatchStatus.Pending);
                    // Loser-side downstream — third-place play-off (single-elim) or LB drop (DE).
                    // A miss in matchById means the target is in another stage we couldn't see
                    // from this match's vantage; treat as pending and let the server enforce.
                    bool loserDownstreamPending = match.NextMatchLoserBracketId == null
                        || !matchById.TryGetValue(match.NextMatchLoserBracketId.Value, out var lbM)
                        || lbM.Status == MatchStatus.Pending;
                    downstreamPending = nextPending && loserDownstreamPending;
                }

                // In approval-required tournaments, only privileged users (admin / hub owner / hub admin)
                // can revert a confirmed result. Participants must contact an admin to dispute it,
                // otherwise the approval gate could be bypassed via the edit flow.
                bool participantCanRevert = isParticipant && !structure.RequireResultApproval;
                match.CanRevert = downstreamPending && (isPrivileged || participantCanRevert);
            }
        }

        private static List<LeagueStandingDto> BuildGroupStandings(List<TournamentParticipantEntity> participants)
        {
            var standings = participants.Select(p => new LeagueStandingDto
            {
                ParticipantId = p.Id!.Value,
                UserId = p.UserId!.Value,
                Name = p.Team?.TeamName ?? p.User?.Username ?? p.UserId!.Value.ToString(),
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
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

            for (int i = 0; i < standings.Count; i++) standings[i].Position = i + 1;
            return standings;
        }

        private void SendNotification(TournamentEntity tournament, Guid tournamentId)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var userIds = await this.AppUnitOfWork.TournamentParticipantRepository.GetAllUserIdsByTournamentId(tournamentId);

                    if (userIds.Count == 0) return;

                    var pushTokens = await this.AppUnitOfWork.UserRepository.GetPushTokensByUserIds(userIds);

                    if (pushTokens.Count > 0)
                    {
                        await notificationService.SendToManyAsync(
                            pushTokens,
                            $"{tournament.Name}",
                            $"Tournament is now live. Good luck!",
                            new { tournamentId });
                    }
                }
                catch { /* fire-and-forget */ }
            });
        }

        #endregion 5. Private Helpers (Core Logic)
    }
}