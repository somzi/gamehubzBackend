using GameHubz.DataModels.Enums;
using MimeKit;

namespace GameHubz.Logic.Services
{
    public class BracketService : AppBaseService
    {
        private readonly HubActivityService hubActivityService;
        private readonly ICacheService cacheService;
        private readonly INotificationService notificationService;

        public BracketService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            HubActivityService hubActivityService,
            ICacheService cacheService,
            INotificationService notificationService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.hubActivityService = hubActivityService;
            this.cacheService = cacheService;
            this.notificationService = notificationService;
        }

        public async Task<TournamentStructureDto> GetTournamentStructure(Guid tournamentId)
        {
            string cacheKey = $"bracket:{tournamentId}";
            var cachedBracket = await cacheService.GetAsync<TournamentStructureDto>(cacheKey);
            if (cachedBracket != null)
                return cachedBracket;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithFullDetails(tournamentId);

            if (tournament == null)
                throw new Exception("Tournament not found");

            var response = new TournamentStructureDto
            {
                TournamentId = tournament.Id!.Value,
                Name = tournament.Name,
                Format = tournament.Format,
                Status = tournament.Status,
                IsTeamTournament = tournament.IsTeamTournament,
                Stages = new List<TournamentStageStructureDto>(),
                HubOwnerId = tournament.Hub!.UserId,
                QualifiersPerGroup = tournament.QualifiersPerGroup
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

                if (stageEntity.Type == StageType.SingleEliminationBracket)
                {
                    stageDto.Rounds = tournament.IsTeamTournament
                        ? MapTeamBracketRounds(stageEntity.TeamMatches)
                        : MapBracketRounds(stageEntity.Matches);
                }
                else if (stageEntity.Type == StageType.GroupStage || stageEntity.Type == StageType.League)
                {
                    stageDto.Groups = await MapGroups(stageEntity);
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
                    await GenerateLeagueTournament(tournamentId, doubleRoundRobin: false, roundDuration: roundDuration);
                    break;

                case TournamentFormat.DoubleElimination:
                    await GenerateDoubleEliminationBracket(tournamentId);
                    break;

                case TournamentFormat.GroupStageWithKnockout:
                    if (!tournament.GroupsCount.HasValue || !tournament.QualifiersPerGroup.HasValue)
                        throw new Exception("Group count and qualifiers count are required for this format.");
                    await GenerateGroupStageWithKnockout(tournamentId, tournament.GroupsCount.Value, tournament.QualifiersPerGroup!.Value, roundDuration);
                    break;

                default:
                    throw new Exception($"Tournament format {tournament.Format} not supported");
            }

            await cacheService.RemoveAsync($"tournament:{tournamentId}");
            await cacheService.RemoveAsync($"bracket:{tournamentId}");
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

            var allMatches = GenerateEliminationMatches(tournamentId, stage.Id.Value, bracketSlots);

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

        public async Task GenerateDoubleEliminationBracket(Guid tournamentId)
        {
            throw new NotImplementedException("Double Elimination logic is coming soon!");
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

            // Save all TeamMatchEntities
            foreach (var tm in allTeamMatches)
            {
                await this.AppUnitOfWork.TeamMatchRepository.AddEntity(tm, this.UserContextReader);
            }

            // Create sub-matches (individual MatchEntities) for each team match with two participants
            foreach (var tm in allTeamMatches)
            {
                if (!tm.HomeTeamParticipantId.HasValue || !tm.AwayTeamParticipantId.HasValue)
                    continue;
                if (tm.Status == TeamMatchStatus.Completed)
                    continue;

                await CreateSubMatchesForTeamMatch(tm, teamSize);
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

            // 1. REVERT LOGIC
            if (match.Status == MatchStatus.Completed)
            {
                if (match.TournamentStage?.Type == StageType.League || match.TournamentStage?.Type == StageType.GroupStage)
                {
                    await RevertLeagueStatistics(match);
                }
                else if (IsElimination(match.TournamentStage?.Type))
                {
                    if (match.TeamMatchId.HasValue)
                        await RevertTeamMatchResult(match);
                    else
                        await RevertEliminationResult(match, nextMatch);
                }
            }

            // 2. Normalize scores — frontend sends HomeScore as the submitter's score
            var currentUser = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            bool isSubmitterAway = match.TeamMatchId.HasValue
                ? match.AwayUserId == currentUser.UserId
                : match.AwayParticipant?.UserId == currentUser.UserId;

            int homeScore = isSubmitterAway ? request.AwayScore : request.HomeScore;
            int awayScore = isSubmitterAway ? request.HomeScore : request.AwayScore;

            match.HomeUserScore = homeScore;
            match.AwayUserScore = awayScore;
            match.Status = MatchStatus.Completed;

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

            // 4. Update match entity
            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);

            // 5. Apply Rules & Advance
            if (match.TournamentStage?.Type == StageType.League || match.TournamentStage?.Type == StageType.GroupStage)
            {
                await UpdateLeagueStatistics(match.HomeParticipant, match.AwayParticipant, homeScore, awayScore);
                await this.SaveAsync();

                await this.AppUnitOfWork.TournamentParticipantRepository.DetachEntity(match.HomeParticipant);
                await this.AppUnitOfWork.TournamentParticipantRepository.DetachEntity(match.AwayParticipant);

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

            await cacheService.RemoveAsync($"bracket:{request.TournamentId}");
            await cacheService.RemoveAsync($"pdf:bracket:{request.TournamentId}");
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

        private async Task UpdateLeagueStatistics(TournamentParticipantEntity? homePart, TournamentParticipantEntity? awayPart, int homeScore, int awayScore)
        {
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
            var homePart = match.HomeParticipant ?? await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(match.HomeParticipantId!.Value);
            var awayPart = match.AwayParticipant ?? await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(match.AwayParticipantId!.Value);

            homePart.GoalsFor -= match.HomeUserScore ?? 0; homePart.GoalsAgainst -= match.AwayUserScore ?? 0;
            awayPart.GoalsFor -= match.AwayUserScore ?? 0; awayPart.GoalsAgainst -= match.HomeUserScore ?? 0;

            if (match.WinnerParticipantId == homePart.Id) { homePart.Wins--; homePart.Points -= 3; awayPart.Losses--; }
            else if (match.WinnerParticipantId == awayPart.Id) { awayPart.Wins--; awayPart.Points -= 3; homePart.Losses--; }
            else { homePart.Draws--; homePart.Points -= 1; awayPart.Draws--; awayPart.Points -= 1; }

            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(homePart, this.UserContextReader);
            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(awayPart, this.UserContextReader);
        }

        private async Task RevertEliminationResult(MatchEntity match, MatchEntity? nextMatch)
        {
            if (nextMatch == null)
            {
                if (match.WinnerParticipantId.HasValue)
                {
                    var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(match.TournamentId);
                    if (tournament.Status == TournamentStatus.Completed)
                    {
                        tournament.Status = TournamentStatus.InProgress;
                        tournament.WinnerUserId = null;
                        await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
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

            await this.AppUnitOfWork.TeamMatchRepository.DetachEntity(teamMatch);

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
            else if (!teamMatch.NextTeamMatchId.HasValue && oldWinner.HasValue)
            {
                var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(teamMatch.TournamentId);
                if (tournament.Status == TournamentStatus.Completed)
                {
                    tournament.Status = TournamentStatus.InProgress;
                    tournament.WinnerUserId = null;
                    tournament.WinnerTeamId = null;
                    await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
                    await this.AppUnitOfWork.TournamentRepository.DetachEntity(tournament);
                }
            }

            await this.SaveAsync();
        }

        private async Task AdvanceWinnerToNextMatch(MatchEntity match, Guid winnerId, Guid? winnerUserId, MatchEntity? nextMatch)
        {
            if (nextMatch != null)
            {
                bool isHomeSlot = (match.MatchOrder % 2) == 0;

                if (isHomeSlot)
                    nextMatch.HomeParticipantId = winnerId;
                else
                    nextMatch.AwayParticipantId = winnerId;

                await this.AppUnitOfWork.MatchRepository.UpdateEntity(nextMatch, this.UserContextReader);
            }
            else
            {
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

        private async Task ProcessTeamMatchResult(MatchEntity completedSubMatch)
        {
            var teamMatch = await this.AppUnitOfWork.TeamMatchRepository.GetByIdWithSubMatches(completedSubMatch.TeamMatchId!.Value);
            if (teamMatch == null) return;

            var subMatches = teamMatch.SubMatches;

            // Check if all sub-matches are completed
            if (!subMatches.All(sm => sm.Status == MatchStatus.Completed))
                return;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(teamMatch.TournamentId);

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

            if (winnerTeamParticipantId == null)
            {
                // Both primary and tiebreaker are tied
                teamMatch.Status = TeamMatchStatus.TieBreakRequired;
                await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(teamMatch, this.UserContextReader);
                await this.SaveAsync();
                await cacheService.RemoveAsync($"bracket:{teamMatch.TournamentId}");
                await cacheService.RemoveAsync($"pdf:bracket:{teamMatch.TournamentId}");
                await cacheService.RemoveAsync($"tournament:{teamMatch.TournamentId}");
                return;
            }

            // Winner determined
            teamMatch.WinnerTeamParticipantId = winnerTeamParticipantId;
            teamMatch.Status = TeamMatchStatus.Completed;
            await this.AppUnitOfWork.TeamMatchRepository.UpdateEntity(teamMatch, this.UserContextReader);

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
                // Tournament is over
                var winnerParticipant = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(winnerTeamParticipantId!.Value);

                if (winnerParticipant.TeamId.HasValue)
                {
                    tournament.WinnerTeamId = winnerParticipant.TeamId;
                }

                tournament.Status = TournamentStatus.Completed;
                await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);
                await this.hubActivityService.LogActivity(tournament.HubId!.Value, tournament.Id!.Value, HubActivityType.TournamentCompleted);
            }

            await this.SaveAsync();

            await cacheService.RemoveAsync($"bracket:{teamMatch.TournamentId}");
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
                    await this.AppUnitOfWork.TournamentParticipantRepository.DetachEntity(q.participant);
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
        {
            int totalRounds = (int)Math.Log2(totalPlayers);
            int roundsFromEnd = totalRounds - roundNumber + 1;
            return roundsFromEnd switch
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
        }

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
            => type == StageType.SingleEliminationBracket || type == StageType.DoubleEliminationWinnersBracket;

        private string GetGroupName(int index)
        {
            const string l = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            return index < l.Length ? l[index].ToString() : (index + 1).ToString();
        }

        private List<BracketRoundDto> MapTeamBracketRounds(List<TeamMatchEntity>? teamMatches)
        {
            var rounds = new List<BracketRoundDto>();
            if (teamMatches == null || !teamMatches.Any()) return rounds;

            var grouped = teamMatches.GroupBy(m => m.RoundNumber ?? 1).OrderBy(g => g.Key);

            foreach (var grp in grouped)
            {
                rounds.Add(new BracketRoundDto
                {
                    RoundNumber = grp.Key,
                    RoundDeadline = grp.SelectMany(m => m.SubMatches).Max(sm => sm.RoundDeadline),
                    Name = $"Round {grp.Key}",
                    Matches = grp.OrderBy(m => m.MatchOrder)
                                 .Select(tm => MapTeamMatchToDto(tm))
                                 .ToList()
                });
            }
            return rounds;
        }

        private MatchStructureDto MapTeamMatchToDto(TeamMatchEntity tm)
        {
            return new MatchStructureDto
            {
                Id = tm.Id!.Value,
                Round = tm.RoundNumber ?? 1,
                Order = tm.MatchOrder ?? 0,
                Status = MapTeamMatchStatus(tm.Status),
                TeamMatchId = tm.Id,
                NextTeamMatchId = tm.NextTeamMatchId,
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

                var dto = new GroupDto
                {
                    GroupId = group.Id!.Value,
                    Name = group.Name,
                    Matches = groupMatches.Select(m => MapMatchToDto(m)).ToList(),
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
                IsRoundLocked = m.RoundOpenAt.HasValue && m.RoundOpenAt.Value > DateTime.UtcNow,
                MatchOpensAt = m.RoundOpenAt,
                Evidences = m.MatchEvidences?.Select(x => x.Url!).ToList() ?? [],
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