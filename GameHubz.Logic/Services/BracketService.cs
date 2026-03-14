using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class BracketService : AppBaseService
    {
        private readonly HubActivityService hubActivityService;
        private readonly ICacheService cacheService;

        public BracketService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            HubActivityService hubActivityService,
            ICacheService cacheService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.hubActivityService = hubActivityService;
            this.cacheService = cacheService;
        }

        public async Task<TournamentStructureDto> GetTournamentStructure(Guid tournamentId)
        {
            string cacheKey = $"bracket:{tournamentId}";
            var cachedBracket = await cacheService.GetAsync<TournamentStructureDto>(cacheKey);
            if (cachedBracket != null)
            {
                return cachedBracket;
            }

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithFullDetails(tournamentId);

            if (tournament == null) throw new Exception("Tournament not found");

            var response = new TournamentStructureDto
            {
                TournamentId = tournament.Id!.Value,
                Name = tournament.Name,
                Format = tournament.Format,
                Status = tournament.Status,
                Stages = new List<TournamentStageStructureDto>(),
                HubOwnerId = tournament.Hub!.UserId
            };

            if (tournament.TournamentStages != null)
            {
                foreach (var stageEntity in tournament.TournamentStages.OrderBy(s => s.Order))
                {
                    var stageDto = new TournamentStageStructureDto
                    {
                        StageId = stageEntity.Id!.Value,
                        Type = stageEntity.Type,
                        Order = stageEntity.Order,
                        Name = stageEntity.Name ?? stageEntity.Type.ToString()
                    };

                    if (stageEntity.Type == StageType.SingleEliminationBracket)
                    {
                        stageDto.Rounds = MapBracketRounds(stageEntity.Matches);
                    }
                    else if (stageEntity.Type == StageType.GroupStage || stageEntity.Type == StageType.League)
                    {
                        stageDto.Groups = await MapGroups(stageEntity);
                    }

                    response.Stages.Add(stageDto);
                }
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

            switch (tournament.Format)
            {
                case TournamentFormat.SingleElimination:
                    await GenerateSingleEliminationBracket(request.TournamentId);
                    break;

                case TournamentFormat.League:
                    await GenerateLeagueTournament(request.TournamentId, doubleRoundRobin: false);
                    break;

                case TournamentFormat.DoubleElimination:
                    await GenerateDoubleEliminationBracket(request.TournamentId);
                    break;

                case TournamentFormat.GroupStageWithKnockout:
                    if (!tournament.GroupsCount.HasValue || !tournament.QualifiersPerGroup.HasValue)
                        throw new Exception("Group count and qualifiers count are required for this format.");
                    await GenerateGroupStageWithKnockout(request.TournamentId, tournament.GroupsCount.Value, tournament.QualifiersPerGroup!.Value);
                    break;

                default:
                    throw new Exception($"Tournament format {tournament.Format} not supported");
            }

            await cacheService.RemoveAsync($"tournament:{request.TournamentId}");
            await cacheService.RemoveAsync($"bracket:{request.TournamentId}");
            await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentLive);
        }

        #endregion 1. Tournament Generation Entry Points

        #region 2. Generators

        public async Task GenerateSingleEliminationBracket(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);

            var participants = tournament!.TournamentParticipants?.ToList();
            if (participants == null || participants.Count == 0) throw new Exception("No participants");

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

            var allMatches = GenerateEliminationMatches(tournamentId, stage.Id.Value, bracketSlots);

            foreach (var match in allMatches)
            {
                await this.AppUnitOfWork.MatchRepository.AddEntity(match, this.UserContextReader);
            }

            tournament.Status = TournamentStatus.InProgress;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            await this.SaveAsync();
        }

        public async Task GenerateLeagueTournament(Guid tournamentId, bool doubleRoundRobin = false)
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
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(p, this.UserContextReader);
            }

            var allMatches = GenerateRoundRobinMatches(tournamentId, stage.Id.Value, participants!, doubleRoundRobin);

            foreach (var match in allMatches)
            {
                match.TournamentGroupId = group.Id;
                await this.AppUnitOfWork.MatchRepository.AddEntity(match, this.UserContextReader);
            }

            tournament.Status = TournamentStatus.InProgress;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            await this.SaveAsync();
        }

        public async Task GenerateGroupStageWithKnockout(Guid tournamentId, int numberOfGroups, int qualifiersPerGroup)
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
            var groupParticipants = new Dictionary<Guid, List<TournamentParticipantEntity>>();
            foreach (var group in groups)
            {
                groupParticipants[group.Id!.Value] = new List<TournamentParticipantEntity>();
            }

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
                groupParticipants[targetGroup.Id.Value].Add(participant);
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

        public async Task GenerateDoubleEliminationBracket(Guid tournamentId)
        {
            throw new NotImplementedException("Double Elimination logic is coming soon!");
        }

        #endregion 2. Generators

        #region 3. Result Processing & Updates

        public async Task UpdateMatchResult(MatchResultDto request)
        {
            var match = await this.AppUnitOfWork.MatchRepository.GetWithStage(request.MatchId);

            if (match == null) throw new Exception("Match not found");
            if (match.TournamentId != request.TournamentId) throw new Exception("Match wrong tournament");

            // ✅ FIX: Load NextMatch only once here to avoid EF Tracking errors
            MatchEntity? nextMatch = null;
            if (match.NextMatchId.HasValue)
            {
                // Try to use navigation property if loaded, otherwise fetch
                if (match.NextMatch != null)
                {
                    nextMatch = match.NextMatch;
                }
                else
                {
                    nextMatch = await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchId.Value);
                }
            }

            // 1. REVERT LOGIC
            if (match.Status == MatchStatus.Completed)
            {
                if (match.TournamentStage?.Type == StageType.League || match.TournamentStage?.Type == StageType.GroupStage)
                {
                    await RevertLeagueStatistics(match);
                }
                else if (IsElimination(match.TournamentStage?.Type))
                {
                    // Pass nextMatch to avoid double tracking
                    await RevertEliminationResult(match, nextMatch);
                }
            }

            // 2. Update Basic Info
            match.HomeUserScore = request.HomeScore;
            match.AwayUserScore = request.AwayScore;
            match.Status = MatchStatus.Completed;

            // 3. Determine Winner
            Guid? winnerParticipientId = null;
            Guid? winnerUserId = null;

            if (request.HomeScore > request.AwayScore)
            {
                winnerParticipientId = match.HomeParticipantId;
                winnerUserId = match.HomeParticipant!.UserId;
            }
            else if (request.AwayScore > request.HomeScore)
            {
                winnerParticipientId = match.AwayParticipantId;
                winnerUserId = match.AwayParticipant!.UserId;
            }

            match.WinnerParticipantId = winnerParticipientId;

            // 4. Update match entity
            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);

            // 5. Apply Rules & Advance
            if (match.TournamentStage?.Type == StageType.League || match.TournamentStage?.Type == StageType.GroupStage)
            {
                await UpdateLeagueStatistics(match, request.HomeScore, request.AwayScore);
                await this.SaveAsync();

                if (match.TournamentStage?.Type == StageType.GroupStage)
                {
                    await CheckAndAdvanceGroupStage(match.TournamentId, match.TournamentStageId!.Value);
                }
                if (match.TournamentStage?.Type == StageType.League)
                {
                    await CheckAndCompleteLeague(match.TournamentId);
                }
            }
            else if (IsElimination(match.TournamentStage?.Type))
            {
                if (winnerParticipientId == null)
                    throw new Exception("Draws not allowed in elimination matches. Someone must win!");

                // Pass nextMatch to avoid double tracking
                await AdvanceWinnerToNextMatch(match, winnerParticipientId.Value, winnerUserId, nextMatch);

                await this.SaveAsync();
            }

            // Clear Cache
            if (match.HomeParticipant?.UserId != null)
                await cacheService.RemoveAsync($"player_stats:{match.HomeParticipant.UserId}");

            if (match.AwayParticipant?.UserId != null)
                await cacheService.RemoveAsync($"player_stats:{match.AwayParticipant.UserId}");

            await cacheService.RemoveAsync($"bracket:{request.TournamentId}");
        }

        #endregion 3. Result Processing & Updates

        #region 4. Data Access (Standings)

        public async Task<List<LeagueStandingDto>> GetLeagueStandings(Guid tournamentId)
        {
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

            return standings;
        }

        #endregion 4. Data Access (Standings)

        #region 5. Private Helpers (Core Logic)

        private async Task UpdateLeagueStatistics(MatchEntity match, int homeScore, int awayScore)
        {
            var homePart = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(match.HomeParticipantId!.Value);
            var awayPart = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(match.AwayParticipantId!.Value);

            homePart.GoalsFor += homeScore; homePart.GoalsAgainst += awayScore;
            awayPart.GoalsFor += awayScore; awayPart.GoalsAgainst += homeScore;

            if (homeScore > awayScore) { homePart.Wins++; homePart.Points += 3; awayPart.Losses++; }
            else if (awayScore > homeScore) { awayPart.Wins++; awayPart.Points += 3; homePart.Losses++; }
            else { homePart.Draws++; homePart.Points += 1; awayPart.Draws++; awayPart.Points += 1; }

            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(homePart, this.UserContextReader);
            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(awayPart, this.UserContextReader);
        }

        private async Task RevertLeagueStatistics(MatchEntity match)
        {
            var homePart = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(match.HomeParticipantId!.Value);
            var awayPart = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(match.AwayParticipantId!.Value);

            homePart.GoalsFor -= match.HomeUserScore ?? 0; homePart.GoalsAgainst -= match.AwayUserScore ?? 0;
            awayPart.GoalsFor -= match.AwayUserScore ?? 0; awayPart.GoalsAgainst -= match.HomeUserScore ?? 0;

            if (match.WinnerParticipantId == homePart.Id) { homePart.Wins--; homePart.Points -= 3; awayPart.Losses--; }
            else if (match.WinnerParticipantId == awayPart.Id) { awayPart.Wins--; awayPart.Points -= 3; homePart.Losses--; }
            else { homePart.Draws--; homePart.Points -= 1; awayPart.Draws--; awayPart.Points -= 1; }

            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(homePart, this.UserContextReader);
            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(awayPart, this.UserContextReader);
        }

        // ✅ FIX: Uses passed nextMatch to check winner reversal
        private async Task RevertEliminationResult(MatchEntity match, MatchEntity? nextMatch)
        {
            if (nextMatch == null) return;

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
                    // Reset completed next match if necessary
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
        }

        // ✅ FIX: Uses passed nextMatch to advance winner
        private async Task AdvanceWinnerToNextMatch(MatchEntity match, Guid winnerId, Guid? winnerUserId, MatchEntity? nextMatch)
        {
            if (nextMatch != null)
            {
                // Logic based on parity of match order
                bool isHomeSlot = (match.MatchOrder % 2) == 0;

                if (isHomeSlot)
                {
                    nextMatch.HomeParticipantId = winnerId;
                }
                else
                {
                    nextMatch.AwayParticipantId = winnerId;
                }

                await this.AppUnitOfWork.MatchRepository.UpdateEntity(nextMatch, this.UserContextReader);
            }
            else
            {
                // Final Match - Tournament Winner
                var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(match.TournamentId);

                tournament.WinnerUserId = winnerUserId;
                tournament.Status = TournamentStatus.Completed;
                await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);

                await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
            }
        }

        private async Task CheckAndCompleteLeague(Guid tournamentId)
        {
            var allMatchesFinished = await this.AppUnitOfWork.MatchRepository.AreAllMatchesFinishedInTournament(tournamentId);

            if (!allMatchesFinished)
                return;

            var tournamentStandings = await this.GetLeagueStandings(tournamentId);
            var winnerUserId = tournamentStandings.Select(s => s.UserId).FirstOrDefault();

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);

            tournament.WinnerUserId = winnerUserId;
            tournament.Status = TournamentStatus.Completed;

            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
        }

        private async Task CheckAndAdvanceGroupStage(Guid tournamentId, Guid groupStageId)
        {
            var allGroupMatches = await this.AppUnitOfWork.MatchRepository.GetByStageId(groupStageId);

            if (allGroupMatches == null || !allGroupMatches.Any()) return;

            bool allMatchesComplete = allGroupMatches.All(m => m.Status == MatchStatus.Completed);

            if (!allMatchesComplete) return;

            var knockoutStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 2);

            if (knockoutStage == null || knockoutStage.Type != StageType.SingleEliminationBracket) return;

            bool hasMatches = await this.AppUnitOfWork.MatchRepository.HasMatchesForStage(knockoutStage.Id!.Value);
            if (hasMatches) return;

            var groupStage = await this.AppUnitOfWork.TournamentStageRepository.GetWithGroupsAndMatches(groupStageId);
            if (groupStage == null || groupStage.TournamentGroups == null) return;

            var qualifiers = new List<(TournamentParticipantEntity participant, int groupRank, string groupName)>();
            int qualifiersPerGroup = groupStage.QualifiedPlayersCount ?? 1;

            foreach (var group in groupStage.TournamentGroups.OrderBy(g => g.Name))
            {
                var groupParticipants = await this.AppUnitOfWork.TournamentParticipantRepository.GetByGroupId(group.Id!.Value);

                var sorted = groupParticipants
                    .OrderByDescending(p => p.Points)
                    .ThenByDescending(p => p.GoalsFor - p.GoalsAgainst)
                    .ThenByDescending(p => p.GoalsFor)
                    .ToList();

                for (int rank = 0; rank < Math.Min(qualifiersPerGroup, sorted.Count); rank++)
                {
                    qualifiers.Add((sorted[rank], rank + 1, group.Name));
                }
            }

            if (qualifiers.Count < 2) throw new Exception("Not enough qualifiers to create knockout bracket.");

            var seededQualifiers = qualifiers
                .OrderBy(q => q.groupRank)
                .ThenBy(q => q.groupName)
                .Select((q, index) =>
                {
                    q.participant.Seed = index + 1;
                    return q.participant;
                })
                .ToList();

            foreach (var p in seededQualifiers)
            {
                await this.AppUnitOfWork.TournamentParticipantRepository.DetachEntity(p);
                var freshParticipant = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(p.Id!.Value);
                freshParticipant.Seed = p.Seed;
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(freshParticipant, this.UserContextReader);
            }

            var bracketSeeded = GetStandardBracketSeeding(seededQualifiers);
            var matches = GenerateEliminationMatches(tournamentId, knockoutStage.Id!.Value, bracketSeeded);

            foreach (var m in matches)
            {
                m.Status = MatchStatus.Pending;
                await this.AppUnitOfWork.MatchRepository.AddEntity(m, this.UserContextReader);
            }

            await this.SaveAsync();
        }

        private List<MatchEntity> GenerateEliminationMatches(Guid tournamentId, Guid stageId, List<TournamentParticipantEntity?> participants)
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
            var circle = new List<TournamentParticipantEntity>();

            for (int i = 0; i < participants.Count; i++)
            {
                circle.Add(participants[i]);
            }
            if (hasBye)
            {
                circle.Add(null!);
            }

            int matchOrder = 0;

            for (int round = 1; round <= totalRounds; round++)
            {
                for (int match = 0; match < matchesPerRound; match++)
                {
                    int homeIndex = match;
                    int awayIndex = n - 1 - match;

                    var homeP = circle[homeIndex];
                    var awayP = circle[awayIndex];

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
                    {
                        circle[i] = circle[i - 1];
                    }
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

        private MatchEntity CreateMatch(Guid tournamentId, Guid stageId, int round, MatchStage stage, int order)
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
                IsUpperBracket = true
            };
        }

        private static MatchStage GetMatchStage(int totalPlayers, int roundNumber)
        {
            int totalRounds = (int)Math.Log2(totalPlayers);
            int roundsFromEnd = totalRounds - roundNumber + 1;
            return roundsFromEnd switch { 1 => MatchStage.Final, 2 => MatchStage.SemiFinal, 3 => MatchStage.QuarterFinal, 4 => MatchStage.RoundOf16, _ => MatchStage.QuarterFinal };
        }

        private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

        private static int GetNextPowerOfTwo(int n)
        {
            int power = 1;
            while (power < n)
            {
                power <<= 1;
            }

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
                    if (!hasHome)
                    {
                        match.Status = MatchStatus.Completed;
                    }

                    continue;
                }

                var winnerId = match.HomeParticipantId ?? match.AwayParticipantId;
                match.WinnerParticipantId = winnerId;
                match.Status = MatchStatus.Completed;

                if (winnerId.HasValue && match.NextMatchId.HasValue && matchesById.TryGetValue(match.NextMatchId.Value, out var nextMatch))
                {
                    bool isHomeSlot = (match.MatchOrder % 2) == 0;
                    if (isHomeSlot)
                    {
                        nextMatch.HomeParticipantId = winnerId;
                    }
                    else
                    {
                        nextMatch.AwayParticipantId = winnerId;
                    }
                }
            }
        }

        private bool IsElimination(StageType? type) => type == StageType.SingleEliminationBracket || type == StageType.DoubleEliminationWinnersBracket;

        private string GetGroupName(int index)
        { const string l = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"; return index < l.Length ? l[index].ToString() : (index + 1).ToString(); }

        private List<BracketRoundDto> MapBracketRounds(List<MatchEntity>? matches)
        {
            var rounds = new List<BracketRoundDto>();
            if (matches == null || !matches.Any()) return rounds;

            var grouped = matches.GroupBy(m => m.RoundNumber ?? 1).OrderBy(g => g.Key);

            foreach (var grp in grouped)
            {
                rounds.Add(new BracketRoundDto
                {
                    RoundNumber = grp.Key,
                    Name = $"Round {grp.Key}",
                    RoundDeadline = grp.Max(m => m.RoundDeadline),
                    Matches = grp.OrderBy(m => m.MatchOrder)
                                 .Select(m => MapMatchToDto(m))
                                 .ToList()
                });
            }
            return rounds;
        }

        private async Task<List<GroupDto>> MapGroups(TournamentStageEntity stage)
        {
            var groupDtos = new List<GroupDto>();
            var groups = stage.TournamentGroups ?? new List<TournamentGroupEntity>();

            foreach (var group in groups)
            {
                var groupMatches = stage.Matches?
                    .Where(m => m.TournamentGroupId == group.Id)
                    .OrderBy(m => m.RoundNumber)
                    .ThenBy(m => m.MatchOrder)
                    .ToList() ?? new List<MatchEntity>();

                var dto = new GroupDto
                {
                    GroupId = group.Id!.Value,
                    Name = group.Name,
                    Matches = groupMatches
                        .Select(m => MapMatchToDto(m))
                        .ToList(),
                    RoundDeadlines = groupMatches
                        .GroupBy(m => m.RoundNumber ?? 1)
                        .OrderBy(g => g.Key)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Max(m => m.RoundDeadline))
                };

                dto.Standings = await GetGroupStandings(group.Id.Value);
                groupDtos.Add(dto);
            }
            return groupDtos;
        }

        private MatchStructureDto MapMatchToDto(MatchEntity m)
        {
            return new MatchStructureDto
            {
                Id = m.Id!.Value,
                Round = m.RoundNumber ?? 1,
                Order = m.MatchOrder ?? 0,
                Status = m.Status,
                StartTime = m.ScheduledStartTime,
                RoundDeadline = m.RoundDeadline,
                NextMatchId = m.NextMatchId,
                Evidences = m.MatchEvidences != null ? m.MatchEvidences.Select(x => x.Url!).ToList() : new List<string>(),
                Home = m.HomeParticipant == null ? null : new MatchParticipantDto
                {
                    ParticipantId = m.HomeParticipant.Id!.Value,
                    UserId = m.HomeParticipant.UserId!.Value,
                    Username = m.HomeParticipant.User?.Username ?? "Unknown",
                    Score = m.HomeUserScore,
                    Seed = m.HomeParticipant.Seed,
                    IsWinner = m.WinnerParticipantId == m.HomeParticipant.Id
                },
                Away = m.AwayParticipant == null ? null : new MatchParticipantDto
                {
                    ParticipantId = m.AwayParticipant.Id!.Value,
                    UserId = m.AwayParticipant.UserId!.Value,
                    Username = m.AwayParticipant.User?.Username ?? "Unknown",
                    Score = m.AwayUserScore,
                    Seed = m.AwayParticipant.Seed,
                    IsWinner = m.WinnerParticipantId == m.AwayParticipant.Id
                }
            };
        }

        private async Task<List<LeagueStandingDto>> GetGroupStandings(Guid groupId)
        {
            var participants = await this.AppUnitOfWork.TournamentParticipantRepository.GetByGroupId(groupId);

            var standings = participants.Select(p => new LeagueStandingDto
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

            for (int i = 0; i < standings.Count; i++) standings[i].Position = i + 1;
            return standings;
        }

        #endregion 5. Private Helpers (Core Logic)
    }
}