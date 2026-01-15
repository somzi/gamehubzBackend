using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class BracketService : AppBaseService
    {
        private readonly IMapper mapper;

        public BracketService(
            IMapper mapper,
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.mapper = mapper;
        }

        public async Task GenerateTournament(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);

            if (tournament == null)
                throw new Exception("Tournament not found");

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

                default:
                    throw new Exception($"Tournament format {tournament.Format} not supported");
            }
        }

        #region 2. Generators (Single Elimination & League)

        /// <summary>
        /// Generates single elimination bracket
        /// </summary>
        public async Task GenerateSingleEliminationBracket(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);
            if (tournament == null) throw new Exception("Tournament not found");

            var participants = tournament.TournamentParticipants?
                .OrderBy(p => p.Seed ?? 999)
                .ToList();

            if (participants == null || participants.Count == 0)
                throw new Exception("No participants found");

            int playerCount = participants.Count;
            if (!IsPowerOfTwo(playerCount))
                throw new Exception($"Player count must be power of 2. Got {playerCount}. Add BYEs or remove players.");

            // Standard Seeding (1vs8, 2vs7...)
            var seededParticipants = GetStandardBracketSeeding(participants);

            var stage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.SingleEliminationBracket,
                Order = 1,
            };

            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(stage, this.UserContextReader);

            var allMatches = GenerateEliminationMatches(tournamentId, stage.Id.Value, seededParticipants);

            foreach (var match in allMatches)
            {
                await this.AppUnitOfWork.MatchRepository.AddEntity(match, this.UserContextReader);
            }

            // Update tournament status
            tournament.Status = TournamentStatus.Live;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);

            await this.SaveAsync();
        }

        /// <summary>
        /// Generates round-robin league
        /// </summary>
        public async Task GenerateLeagueTournament(Guid tournamentId, bool doubleRoundRobin = false)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);
            if (tournament == null) throw new Exception("Tournament not found");

            var participants = tournament.TournamentParticipants?.ToList();
            if (participants == null || participants.Count < 2)
                throw new Exception("Need at least 2 participants for a league");

            var stage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.League,
                Order = 1,
            };

            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(stage, this.UserContextReader);

            // Optional: Create a "Group" container if your DB requires it, otherwise just link to Stage
            // logic here...

            var allMatches = GenerateRoundRobinMatches(tournamentId, stage.Id.Value, participants, doubleRoundRobin);

            foreach (var match in allMatches)
            {
                await this.AppUnitOfWork.MatchRepository.AddEntity(match, this.UserContextReader);
            }

            // Update tournament status
            tournament.Status = TournamentStatus.Live;
            await this.AppUnitOfWork.TournamentRepository.UpdateEntity(tournament, this.UserContextReader);

            await this.SaveAsync();
        }

        /// <summary>
        /// Placeholder for Double Elimination (To be implemented later)
        /// </summary>
        public async Task GenerateDoubleEliminationBracket(Guid tournamentId)
        {
            throw new NotImplementedException("Double Elimination logic is coming soon!");
        }

        #endregion 2. Generators (Single Elimination & League)

        #region 3. Result Processing (The Core Logic)

        /// <summary>
        /// Universal method to update match results. Handles League points AND Bracket progression.
        /// Supports editing scores (reverting previous stats).
        /// </summary>
        public async Task UpdateMatchResult(MatchResultDto request)
        {
            // Load match with stage info to know rules
            var match = await this.AppUnitOfWork.MatchRepository.GetWithStage(request.MatchId);

            if (match == null) throw new Exception("Match not found");
            if (match.TournamentId != request.TournamentId) throw new Exception("Match does not belong to this tournament");

            // 1. REVERT LOGIC: If match was ALREADY completed, undo previous stats to allow correction
            if (match.Status == MatchStatus.Completed)
            {
                if (match.TournamentStage?.Type == StageType.League)
                {
                    await RevertLeagueStatistics(match);
                }
                // Note: For elimination, we allow edits only if the NEXT match hasn't started yet.
                // You can add that validation here later.
            }

            // 2. Update Basic Scores
            match.HomeUserScore = request.HomeScore;
            match.AwayUserScore = request.AwayScore;
            match.Status = MatchStatus.Completed;

            // 3. Determine Winner
            Guid? winnerId = null;
            if (request.HomeScore > request.AwayScore) winnerId = match.HomeParticipantId;
            else if (request.AwayScore > request.HomeScore) winnerId = match.AwayParticipantId;

            match.WinnerParticipantId = winnerId;

            // 4. Apply New Logic (Points or Advancement)
            if (match.TournamentStage?.Type == StageType.League)
            {
                await UpdateLeagueStatistics(match, request.HomeScore, request.AwayScore);
            }
            else if (IsElimination(match.TournamentStage?.Type))
            {
                if (winnerId == null) throw new Exception("Draws are not allowed in elimination matches.");
                await AdvanceWinnerToNextMatch(match, winnerId.Value);
            }

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();
        }

        #endregion 3. Result Processing (The Core Logic)

        #region 4. Data Access (Standings)

        public async Task<List<LeagueStandingDto>> GetLeagueStandings(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);
            if (tournament == null) throw new Exception("Tournament not found");

            var standings = tournament.TournamentParticipants?
                .Select(p => new LeagueStandingDto
                {
                    ParticipantId = p.Id!.Value,
                    UserId = p.UserId!.Value, // Changed from p.UserId.Value in case User is deleted, check nullability
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

            for (int i = 0; i < standings!.Count; i++)
            {
                standings[i].Position = i + 1;
            }

            return standings;
        }

        #endregion 4. Data Access (Standings)

        #region 5. Private Helpers (Logic Internals)

        private async Task UpdateLeagueStatistics(MatchEntity match, int homeScore, int awayScore)
        {
            var homePart = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(match.HomeParticipantId!.Value);
            var awayPart = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(match.AwayParticipantId!.Value);

            // Update goals
            homePart.GoalsFor += homeScore;
            homePart.GoalsAgainst += awayScore;
            awayPart.GoalsFor += awayScore;
            awayPart.GoalsAgainst += homeScore;

            // Update points
            if (homeScore > awayScore)
            {
                homePart.Wins++;
                homePart.Points += 3;
                awayPart.Losses++;
            }
            else if (awayScore > homeScore)
            {
                awayPart.Wins++;
                awayPart.Points += 3;
                homePart.Losses++;
            }
            else
            {
                homePart.Draws++;
                homePart.Points += 1;
                awayPart.Draws++;
                awayPart.Points += 1;
            }

            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(homePart, this.UserContextReader);
            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(awayPart, this.UserContextReader);
        }

        private async Task RevertLeagueStatistics(MatchEntity match)
        {
            // IMPORTANT: Fetch FRESH data from DB to ensure we subtract from current total
            var homePart = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(match.HomeParticipantId!.Value);
            var awayPart = await this.AppUnitOfWork.TournamentParticipantRepository.GetByIdOrThrowIfNull(match.AwayParticipantId!.Value);

            // Subtract OLD goals
            homePart.GoalsFor -= match.HomeUserScore ?? 0;
            homePart.GoalsAgainst -= match.AwayUserScore ?? 0;
            awayPart.GoalsFor -= match.AwayUserScore ?? 0;
            awayPart.GoalsAgainst -= match.HomeUserScore ?? 0;

            // Subtract OLD Points
            if (match.WinnerParticipantId == homePart.Id)
            {
                homePart.Wins--;
                homePart.Points -= 3;
                awayPart.Losses--;
            }
            else if (match.WinnerParticipantId == awayPart.Id)
            {
                awayPart.Wins--;
                awayPart.Points -= 3;
                homePart.Losses--;
            }
            else // Draw
            {
                homePart.Draws--;
                homePart.Points -= 1;
                awayPart.Draws--;
                awayPart.Points -= 1;
            }

            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(homePart, this.UserContextReader);
            await this.AppUnitOfWork.TournamentParticipantRepository.UpdateEntity(awayPart, this.UserContextReader);
        }

        private async Task AdvanceWinnerToNextMatch(MatchEntity match, Guid winnerId)
        {
            if (match.NextMatchId.HasValue)
            {
                var nextMatch = await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchId.Value);
                if (nextMatch != null)
                {
                    // Logic: Fill the first empty slot.
                    // If Home is empty, go there. If Home is filled but Away is empty, go there.
                    if (!nextMatch.HomeParticipantId.HasValue)
                        nextMatch.HomeParticipantId = winnerId;
                    else if (!nextMatch.AwayParticipantId.HasValue)
                        nextMatch.AwayParticipantId = winnerId;

                    await this.AppUnitOfWork.MatchRepository.UpdateEntity(nextMatch, this.UserContextReader);
                }
            }
        }

        private List<MatchEntity> GenerateEliminationMatches(Guid tournamentId, Guid stageId, List<TournamentParticipantEntity> participants)
        {
            int playerCount = participants.Count;
            int totalRounds = (int)Math.Log2(playerCount);

            var allMatches = new List<MatchEntity>();
            var currentRoundMatches = new List<MatchEntity>();

            int matchesInRound = playerCount / 2;

            // ROUND 1: Participants vs Participants
            for (int i = 0; i < matchesInRound; i++)
            {
                var match = CreateMatch(tournamentId, stageId, 1, GetMatchStage(playerCount, 1), i);
                match.HomeParticipantId = participants[i * 2].Id;
                match.AwayParticipantId = participants[i * 2 + 1].Id;

                currentRoundMatches.Add(match);
                allMatches.Add(match);
            }

            // SUBSEQUENT ROUNDS: Winner vs Winner (Empty initially)
            for (int round = 2; round <= totalRounds; round++)
            {
                matchesInRound /= 2;
                var nextRoundMatches = new List<MatchEntity>();

                for (int i = 0; i < matchesInRound; i++)
                {
                    var match = CreateMatch(tournamentId, stageId, round, GetMatchStage(playerCount, round), i);

                    // Link previous matches to this new one
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

            // Add dummy for odd number of players
            bool hasBye = n % 2 != 0;
            if (hasBye) n++;

            int totalRounds = n - 1;
            int matchesPerRound = n / 2;

            var circle = participants.ToList(); // Copy list to manipulate
            int matchOrder = 0;

            for (int round = 1; round <= totalRounds; round++)
            {
                for (int match = 0; match < matchesPerRound; match++)
                {
                    int homeIndex = match;
                    int awayIndex = n - 1 - match;

                    // If index out of bounds (odd player count case), it's a Bye
                    if (hasBye && (homeIndex >= participants.Count || awayIndex >= participants.Count))
                        continue;

                    var homeParticipant = homeIndex < participants.Count ? circle[homeIndex] : null;
                    var awayParticipant = awayIndex < participants.Count ? circle[awayIndex] : null;

                    if (homeParticipant != null && awayParticipant != null)
                    {
                        var matchEntity = new MatchEntity
                        {
                            Id = Guid.NewGuid(),
                            TournamentId = tournamentId,
                            TournamentStageId = stageId,
                            RoundNumber = round,
                            HomeParticipantId = homeParticipant.Id,
                            AwayParticipantId = awayParticipant.Id,
                            Status = MatchStatus.Scheduled,
                            Stage = MatchStage.GroupStage,
                            MatchOrder = matchOrder++,
                            IsUpperBracket = true
                        };
                        allMatches.Add(matchEntity);
                    }
                }

                // Rotate logic
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
                    var originalMatch = allMatches[i];
                    var returnMatch = new MatchEntity
                    {
                        Id = Guid.NewGuid(),
                        TournamentId = tournamentId,
                        TournamentStageId = stageId,
                        RoundNumber = originalMatch!.RoundNumber!.Value + totalRounds,
                        HomeParticipantId = originalMatch.AwayParticipantId, // Swap
                        AwayParticipantId = originalMatch.HomeParticipantId, // Swap
                        Status = MatchStatus.Scheduled,
                        Stage = MatchStage.GroupStage,
                        MatchOrder = matchOrder++,
                        IsUpperBracket = true
                    };
                    allMatches.Add(returnMatch);
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

            var result = new List<TournamentParticipantEntity>();
            foreach (var index in bracketOrder)
            {
                result.Add(sorted[index]);
            }
            return result;
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
                Status = MatchStatus.Scheduled,
                IsUpperBracket = true
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
                _ => MatchStage.QuarterFinal
            };
        }

        private static bool IsPowerOfTwo(int n)
        {
            return n > 0 && (n & (n - 1)) == 0;
        }

        private bool IsElimination(StageType? type)
        {
            return type == StageType.SingleEliminationBracket ||
                   type == StageType.DoubleEliminationWinnersBracket ||
                   type == StageType.DoubleEliminationLosersBracket;
        }

        #endregion 5. Private Helpers (Logic Internals)
    }
}