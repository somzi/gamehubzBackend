using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class BracketService : AppBaseService
    {
        public BracketService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
        }

        public async Task<TournamentStructureDto> GetTournamentStructure(Guid tournamentId)
        {
            // NOTE: Ensure your Repo has GetWithFullDetails or similar to load Stages -> Groups/Matches -> Participants -> User
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithFullDetails(tournamentId);

            if (tournament == null) throw new Exception("Tournament not found");

            var response = new TournamentStructureDto
            {
                TournamentId = tournament.Id!.Value,
                Name = tournament.Name,
                Format = tournament.Format,
                Status = tournament.Status,
                Stages = new List<TournamentStageStructureDto>()
            };

            // Map Stages
            if (tournament.TournamentStages != null)
            {
                foreach (var stageEntity in tournament.TournamentStages.OrderBy(s => s.Order))
                {
                    // ✅ Using the new class name here
                    var stageDto = new TournamentStageStructureDto
                    {
                        StageId = stageEntity.Id!.Value,
                        Type = stageEntity.Type,
                        Order = stageEntity.Order,
                        Name = stageEntity.Name ?? stageEntity.Type.ToString()
                    };

                    // CASE A: Bracket (Single Elimination)
                    if (stageEntity.Type == StageType.SingleEliminationBracket)
                    {
                        stageDto.Rounds = MapBracketRounds(stageEntity.Matches);
                    }
                    // CASE B: Groups / League
                    else if (stageEntity.Type == StageType.GroupStage || stageEntity.Type == StageType.League)
                    {
                        stageDto.Groups = await MapGroups(stageEntity);
                    }

                    response.Stages.Add(stageDto);
                }
            }

            return response;
        }

        #region 1. Tournament Generation Entry Points

        /// <summary>
        /// Main entry point - generates structure based on tournament format
        /// </summary>
        public async Task CreateBracket(Guid tournamentId, int? groupsCount = null, int? qualifiersPerGroup = null)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);

            if (tournament == null)
                throw new Exception("Tournament not found");

            // Ensure we have participants before starting
            if (tournament.TournamentParticipants == null || tournament.TournamentParticipants.Count < 2)
                throw new Exception("Not enough participants to start tournament.");

            switch (tournament.Format)
            {
                case TournamentFormat.SingleElimination:
                    await GenerateSingleEliminationBracket(tournamentId);
                    break;

                case TournamentFormat.League:
                    await GenerateLeagueTournament(tournamentId, doubleRoundRobin: false);
                    break;

                case TournamentFormat.DoubleElimination:
                    await GenerateDoubleEliminationBracket(tournamentId);
                    break;

                case TournamentFormat.GroupStageWithKnockout:
                    if (!groupsCount.HasValue || !qualifiersPerGroup.HasValue)
                        throw new Exception("Group count and qualifiers count are required for this format.");
                    await GenerateGroupStageWithKnockout(tournamentId, groupsCount.Value, qualifiersPerGroup.Value);
                    break;

                default:
                    throw new Exception($"Tournament format {tournament.Format} not supported");
            }
        }

        #endregion 1. Tournament Generation Entry Points

        #region 2. Generators

        /// <summary>
        /// Generates single elimination bracket tree
        /// </summary>
        public async Task GenerateSingleEliminationBracket(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);

            // 1. Load participants
            var participants = tournament!.TournamentParticipants?.ToList();
            if (participants == null || participants.Count == 0) throw new Exception("No participants");

            // 2. CHECK: Power of 2
            if (!IsPowerOfTwo(participants.Count))
                throw new Exception($"Player count must be power of 2. Got {participants.Count}.");

            // =========================================================================
            // 3. RANDOMIZE (SHUFFLE)
            // =========================================================================
            var rng = new Random();
            // Shuffle the list randomly
            var shuffledParticipants = participants.OrderBy(a => rng.Next()).ToList();

            // Assign Seeds based on this random order (1, 2, 3, 4...)
            // This ensures "Standard Seeding" logic works on a random set of people
            for (int i = 0; i < shuffledParticipants.Count; i++)
            {
                shuffledParticipants[i].Seed = i + 1;
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(shuffledParticipants[i], this.UserContextReader);
            }

            // 4. Create Stage
            var stage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.SingleEliminationBracket,
                Order = 1,
                Name = "Main Bracket"
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(stage, this.UserContextReader);

            // 5. Generate with newly assigned random seeds
            // This will pair Seed 1 vs Seed 8, but Seed 1 is now a random person.
            var seededParticipants = GetStandardBracketSeeding(shuffledParticipants);
            var allMatches = GenerateEliminationMatches(tournamentId, stage.Id.Value, seededParticipants);

            foreach (var match in allMatches)
            {
                await this.AppUnitOfWork.MatchRepository.AddEntity(match, this.UserContextReader);
            }

            // 6. Update Status
            tournament.Status = TournamentStatus.InProgress;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            await this.SaveAsync();
        }

        /// <summary>
        /// Generates round-robin league (1 big group)
        /// </summary>
        public async Task GenerateLeagueTournament(Guid tournamentId, bool doubleRoundRobin = false)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);

            // 1. Load & Shuffle
            var rng = new Random();
            var participants = tournament!.TournamentParticipants?
                .OrderBy(x => rng.Next()) // <--- RANDOMIZE HERE
                .ToList();

            // Create Stage
            var stage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.League,
                Order = 1,
                Name = "League Season"
            };
            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(stage, this.UserContextReader);

            // Create Default Group
            var group = new TournamentGroupEntity
            {
                Id = Guid.NewGuid(),
                TournamentStageId = stage.Id,
                Name = "League Table"
            };
            await this.AppUnitOfWork.TournamentGroupRepository.AddEntity(group, this.UserContextReader);

            // Link players
            foreach (var p in participants!)
            {
                p.TournamentGroupId = group.Id;
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(p, this.UserContextReader);
            }

            // Generate Matches (Now using randomized list)
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

        /// <summary>
        /// Generates World Cup Style: Groups first, then Knockout
        /// </summary>
        public async Task GenerateGroupStageWithKnockout(Guid tournamentId, int numberOfGroups, int qualifiersPerGroup)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);
            var participants = tournament!.TournamentParticipants?.ToList();

            if (participants!.Count < numberOfGroups * 2)
                throw new Exception($"Not enough participants. Need at least {numberOfGroups * 2} players for {numberOfGroups} groups.");

            // Validate Knockout Size
            int totalQualifiers = numberOfGroups * qualifiersPerGroup;
            if (!IsPowerOfTwo(totalQualifiers))
                throw new Exception($"Total qualifiers ({totalQualifiers}) must be a power of 2 (4, 8, 16, 32) for the bracket to work.");

            // 1. Create Group Stage
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

            // 2. Create Group Entities
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

            // 3. Shuffle & Distribute Players (Snake Draft)
            var seededParticipants = participants.OrderBy(p => p.Seed ?? 999).ToList();

            for (int i = 0; i < seededParticipants.Count; i++)
            {
                // Snake draft logic for fair distribution
                int groupIndex = (i / numberOfGroups) % 2 == 0
                    ? i % numberOfGroups
                    : numberOfGroups - 1 - (i % numberOfGroups);

                var participant = seededParticipants[i];
                var targetGroup = groups[groupIndex];

                // Update Participant
                participant.TournamentGroupId = targetGroup.Id;
                participant.Points = 0; participant.Wins = 0; participant.Draws = 0;
                participant.Losses = 0; participant.GoalsFor = 0; participant.GoalsAgainst = 0;

                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(participant, this.UserContextReader);

                // Add to local list for match gen
                if (targetGroup.Participants == null) targetGroup.Participants = new List<TournamentParticipantEntity>();
                targetGroup.Participants.Add(participant);
            }

            // 4. Generate Matches per Group
            var allMatches = new List<MatchEntity>();
            foreach (var group in groups)
            {
                if (group.Participants == null || group.Participants.Count < 2) continue;

                var groupMatches = GenerateRoundRobinMatches(tournamentId, groupStage.Id.Value, group.Participants, false);
                foreach (var m in groupMatches)
                {
                    m.TournamentGroupId = group.Id;
                    m.Stage = MatchStage.GroupStage;
                    allMatches.Add(m);
                }
            }

            // 5. Save Matches
            foreach (var match in allMatches)
            {
                await this.AppUnitOfWork.MatchRepository.AddEntity(match, this.UserContextReader);
            }

            // 6. Create EMPTY Knockout Stage (Placeholder for later)
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

            // 1. REVERT LOGIC (If editing a completed match)
            if (match.Status == MatchStatus.Completed)
            {
                if (match.TournamentStage?.Type == StageType.League || match.TournamentStage?.Type == StageType.GroupStage)
                {
                    await RevertLeagueStatistics(match);
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

            // 4. Apply Rules
            if (match.TournamentStage?.Type == StageType.League || match.TournamentStage?.Type == StageType.GroupStage)
            {
                // League/Group: Update Points
                await UpdateLeagueStatistics(match, request.HomeScore, request.AwayScore);

                // If Group Stage, check if we need to advance to Knockout
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
                // Bracket: Advance Winner
                if (winnerParticipientId == null) throw new Exception("Draws not allowed in elimination.");
                await AdvanceWinnerToNextMatch(match, winnerParticipientId.Value, winnerUserId);
            }

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();
        }

        private async Task CheckAndCompleteLeague(Guid tournamentId)
        {
            var allMatchesFinished = await this.AppUnitOfWork.MatchRepository.AreAllMatchesFinishedInTournament(tournamentId);

            // If any match is NOT completed, the league is still ongoing
            if (!allMatchesFinished)
                return;

            var tournamentStandings = await this.GetLeagueStandings(tournamentId);

            var winnerUserId = tournamentStandings.Select(s => s.UserId).FirstOrDefault();

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);

            tournament.WinnerUserId = winnerUserId;
            tournament.Status = TournamentStatus.Completed;

            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
        }

        private async Task CheckAndAdvanceGroupStage(Guid tournamentId, Guid groupStageId)
        {
            // 1. Get stage with all data (Assumes Repo method: GetWithGroupsAndMatches)
            var groupStage = await this.AppUnitOfWork.TournamentStageRepository.GetWithGroupsAndMatches(groupStageId);
            if (groupStage == null) return;

            // 2. Check if ALL group matches are completed
            // If repository didn't load matches, you might need: var allMatches = await _matchRepo.GetByStageId(groupStageId);
            if (groupStage.Matches == null || groupStage.Matches.Any(m => m.Status != MatchStatus.Completed))
                return; // Not ready yet

            // 3. Get knockout stage (Assumes Repo method: GetByOrder(tournamentId, order))
            // Or use generic Get and filter in memory if necessary, but repo method is better.
            var knockoutStage = await this.AppUnitOfWork.TournamentStageRepository.GetByOrder(tournamentId, 2);

            if (knockoutStage == null || knockoutStage.Type != StageType.SingleEliminationBracket) return;

            // 4. Check if bracket already generated (prevent duplicates)
            // Assumes Repo method: HasMatches(stageId)
            bool hasMatches = await this.AppUnitOfWork.MatchRepository.HasMatchesForStage(knockoutStage.Id!.Value);
            if (hasMatches) return;

            // 5. Calculate Qualifiers with Ranking
            var qualifiers = new List<(TournamentParticipantEntity participant, int groupRank, string groupName)>();
            int qualifiersPerGroup = groupStage.QualifiedPlayersCount ?? 1;

            foreach (var group in groupStage.TournamentGroups!.OrderBy(g => g.Name))
            {
                // Assumes Repo method: GetByGroupId
                var groupParticipants = await this.AppUnitOfWork.TournamentParticipantRepository.GetByGroupId(group.Id!.Value);

                var sorted = groupParticipants
                    .OrderByDescending(p => p.Points)
                    .ThenByDescending(p => p.GoalsFor - p.GoalsAgainst)
                    .ThenByDescending(p => p.GoalsFor)
                    .ToList();

                // Track rank
                for (int rank = 0; rank < Math.Min(qualifiersPerGroup, sorted.Count); rank++)
                {
                    qualifiers.Add((sorted[rank], rank + 1, group.Name));
                }
            }

            // 6. Seed Qualifiers: All 1st place finishers first, then all 2nd place
            var seededQualifiers = qualifiers
                .OrderBy(q => q.groupRank)    // 1st place winners first
                .ThenBy(q => q.groupName)     // Then alphabetically by group
                .Select((q, index) =>
                {
                    q.participant.Seed = index + 1;
                    return q.participant;
                })
                .ToList();

            // 7. Update seeds in DB
            foreach (var p in seededQualifiers)
            {
                await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(p, this.UserContextReader);
            }

            // 8. Generate Bracket
            var bracketSeeded = GetStandardBracketSeeding(seededQualifiers);
            var matches = GenerateEliminationMatches(tournamentId, knockoutStage.Id!.Value, bracketSeeded);

            foreach (var m in matches)
            {
                await this.AppUnitOfWork.MatchRepository.AddEntity(m, this.UserContextReader);
            }

            await this.SaveAsync();
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

        private async Task AdvanceWinnerToNextMatch(MatchEntity match, Guid winnerId, Guid? winnerUserId)
        {
            if (match.NextMatchId.HasValue)
            {
                var nextMatch = await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchId.Value);
                if (nextMatch != null)
                {
                    if (!nextMatch.HomeParticipantId.HasValue) nextMatch.HomeParticipantId = winnerId;
                    else if (!nextMatch.AwayParticipantId.HasValue) nextMatch.AwayParticipantId = winnerId;
                    await this.AppUnitOfWork.MatchRepository.UpdateEntity(nextMatch, this.UserContextReader);
                }
            }
            else
            {
                var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(match.TournamentId);

                tournament.WinnerUserId = winnerUserId;
                tournament.Status = TournamentStatus.Completed;
                await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
            }
        }

        private List<MatchEntity> GenerateEliminationMatches(Guid tournamentId, Guid stageId, List<TournamentParticipantEntity> participants)
        {
            int playerCount = participants.Count;
            int totalRounds = (int)Math.Log2(playerCount);
            var allMatches = new List<MatchEntity>();
            var currentRoundMatches = new List<MatchEntity>();
            int matchesInRound = playerCount / 2;

            for (int i = 0; i < matchesInRound; i++)
            {
                var match = CreateMatch(tournamentId, stageId, 1, GetMatchStage(playerCount, 1), i);
                match.HomeParticipantId = participants[i * 2].Id;
                match.AwayParticipantId = participants[i * 2 + 1].Id;
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
            return allMatches;
        }

        private List<MatchEntity> GenerateRoundRobinMatches(Guid tournamentId, Guid stageId, List<TournamentParticipantEntity> participants, bool doubleRoundRobin)
        {
            var allMatches = new List<MatchEntity>();
            int n = participants.Count;
            bool hasBye = n % 2 != 0;
            if (hasBye) n++;

            int totalRounds = n - 1;
            int matchesPerRound = n / 2;
            var circle = participants.ToList();
            int matchOrder = 0;

            for (int round = 1; round <= totalRounds; round++)
            {
                for (int match = 0; match < matchesPerRound; match++)
                {
                    int homeIndex = match;
                    int awayIndex = n - 1 - match;
                    if (hasBye && (homeIndex >= participants.Count || awayIndex >= participants.Count)) continue;

                    var homeP = homeIndex < participants.Count ? circle[homeIndex] : null;
                    var awayP = awayIndex < participants.Count ? circle[awayIndex] : null;

                    if (homeP != null && awayP != null)
                    {
                        var m = CreateMatch(tournamentId, stageId, round, MatchStage.GroupStage, matchOrder++);
                        m.HomeParticipantId = homeP.Id; m.AwayParticipantId = awayP.Id;
                        allMatches.Add(m);
                    }
                }
                // Rotate
                if (round < totalRounds)
                {
                    var temp = circle[n - 1];
                    for (int i = n - 1; i > 1; i--) circle[i] = circle[i - 1];
                    circle[1] = temp;
                }
            }

            if (doubleRoundRobin)
            {
                int count = allMatches.Count;
                for (int i = 0; i < count; i++)
                {
                    var orig = allMatches[i];
                    var ret = CreateMatch(tournamentId, stageId, orig.RoundNumber!.Value + totalRounds, MatchStage.GroupStage, matchOrder++);
                    ret.HomeParticipantId = orig.AwayParticipantId; ret.AwayParticipantId = orig.HomeParticipantId;
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

        private bool IsElimination(StageType? type) => type == StageType.SingleEliminationBracket || type == StageType.DoubleEliminationWinnersBracket;

        private string GetGroupName(int index)
        { const string l = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"; return index < l.Length ? l[index].ToString() : (index + 1).ToString(); }

        #endregion 5. Private Helpers (Core Logic)

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
                var dto = new GroupDto
                {
                    GroupId = group.Id!.Value,
                    Name = group.Name,
                    Matches = stage.Matches?
                        .Where(m => m.TournamentGroupId == group.Id)
                        .OrderBy(m => m.RoundNumber)
                        .ThenBy(m => m.MatchOrder)
                        .Select(m => MapMatchToDto(m))
                        .ToList() ?? new List<MatchStructureDto>() // ✅ Updated List Type
                };

                dto.Standings = await GetGroupStandings(group.Id.Value);
                groupDtos.Add(dto);
            }
            return groupDtos;
        }

        // ✅ Updated Return Type
        private MatchStructureDto MapMatchToDto(MatchEntity m)
        {
            return new MatchStructureDto
            {
                Id = m.Id!.Value,
                Order = m.MatchOrder ?? 0,
                Status = m.Status,
                StartTime = m.ScheduledStartTime,
                NextMatchId = m.NextMatchId,
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
    }
}