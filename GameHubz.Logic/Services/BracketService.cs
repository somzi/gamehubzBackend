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

        public async Task GenerateSingleEliminationBracket(Guid tournamentId)
        {
            // 1. Fetch Tournament and Participants
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithParticipents(tournamentId);

            if (tournament == null)
                throw new Exception("Tournament not found");

            var participants = tournament.TournamentParticipants?
                .OrderBy(p => p.Seed ?? 999)
                .ToList();

            if (participants == null || participants.Count == 0)
                throw new Exception("No participants found");

            int playerCount = participants.Count;
            if (!IsPowerOfTwo(playerCount))
                throw new Exception($"Player count must be power of 2. Got {playerCount}");

            // Apply proper bracket seeding (1v8, 4v5, 3v6, 2v7)
            var seededParticipants = GetStandardBracketSeeding(participants);

            // 2. Create the Stage
            var stage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = StageType.SingleEliminationBracket,
                Order = 1
            };

            await this.AppUnitOfWork.TournamentStageRepository.AddEntity(stage, this.UserContextReader);

            var allMatches = GenerateMatches(tournamentId, stage.Id.Value, seededParticipants);

            // 4. Save to database
            foreach (var match in allMatches)
            {
                await this.AppUnitOfWork.MatchRepository.AddEntity(match, this.UserContextReader);
            }

            await this.SaveAsync();
        }

        public async Task UpdateMatchResult(MatchResultDto request)
        {
            var match = await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(request.MatchId);

            if (match.TournamentId != request.TournamentId)
                throw new Exception("Match does not belong to the specified tournament");

            if (match == null)
                throw new Exception("Match not found");

            if (match.Status == MatchStatus.Completed)
                throw new Exception("Match already completed");

            // Update scores
            match.HomeUserScore = request.HomeScore;
            match.AwayUserScore = request.AwayScore;
            match.Status = MatchStatus.Completed;

            // Determine winner
            Guid? winnerId = null;
            if (request.HomeScore > request.AwayScore)
                winnerId = match.HomeParticipantId;
            else if (request.AwayScore > request.HomeScore)
                winnerId = match.AwayParticipantId;
            else
                throw new Exception("Draws not allowed in elimination format");

            match.WinnerParticipantId = winnerId;

            // Advance winner to next match
            if (match.NextMatchId.HasValue)
            {
                var nextMatch = await this.AppUnitOfWork.MatchRepository.GetByIdOrThrowIfNull(match.NextMatchId.Value);

                if (nextMatch != null)
                {
                    // Place winner in next match
                    if (!nextMatch.HomeParticipantId.HasValue)
                        nextMatch.HomeParticipantId = winnerId;
                    else if (!nextMatch.AwayParticipantId.HasValue)
                        nextMatch.AwayParticipantId = winnerId;

                    await this.AppUnitOfWork.MatchRepository.UpdateEntity(nextMatch, this.UserContextReader);
                }
            }

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await SaveAsync();
        }

        private List<MatchEntity> GenerateMatches(
            Guid tournamentId,
            Guid stageId,
            List<TournamentParticipantEntity> participants)
        {
            int playerCount = participants.Count;
            int totalRounds = (int)Math.Log2(playerCount);
            var allMatches = new List<MatchEntity>();
            var currentRoundMatches = new List<MatchEntity>();

            // Round 1 - assign all players
            int matchesInRound = playerCount / 2;
            for (int i = 0; i < matchesInRound; i++)
            {
                var match = CreateMatch(
                    tournamentId,
                    stageId,
                    1,
                    GetMatchStage(playerCount, 1),
                    i
                );
                // ✅ Fixed: Use participant.Id instead of participant.UserId
                match.HomeParticipantId = participants[i * 2].Id;
                match.AwayParticipantId = participants[i * 2 + 1].Id;

                currentRoundMatches.Add(match);
                allMatches.Add(match);
            }

            // Subsequent rounds
            for (int round = 2; round <= totalRounds; round++)
            {
                matchesInRound /= 2;
                var nextRoundMatches = new List<MatchEntity>();

                for (int i = 0; i < matchesInRound; i++)
                {
                    var match = CreateMatch(
                        tournamentId,
                        stageId,
                        round,
                        GetMatchStage(playerCount, round),
                        i
                    );

                    // Link previous matches
                    currentRoundMatches[i * 2].NextMatchId = match.Id;
                    currentRoundMatches[i * 2 + 1].NextMatchId = match.Id;

                    nextRoundMatches.Add(match);
                    allMatches.Add(match);
                }

                currentRoundMatches = nextRoundMatches;
            }

            return allMatches;
        }

        /// <summary>
        /// Standard bracket seeding algorithm (1v8, 4v5, 3v6, 2v7 for 8 players)
        /// Ensures top seeds can only meet in later rounds
        /// </summary>
        private List<TournamentParticipantEntity> GetStandardBracketSeeding(List<TournamentParticipantEntity> participants)
        {
            var sorted = participants.OrderBy(x => x.Seed ?? 999).ToList();
            int n = sorted.Count;
            var bracketOrder = new List<int> { 0 }; // Start with seed 1

            int count = 1;
            while (count < n)
            {
                var newOrder = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    newOrder.Add(bracketOrder[i]); // Higher seed
                    newOrder.Add(count * 2 - 1 - bracketOrder[i]); // Opponent (lower seed)
                }
                bracketOrder = newOrder;
                count *= 2;
            }

            // Map indices back to participant objects
            var result = new List<TournamentParticipantEntity>();
            foreach (var index in bracketOrder)
            {
                result.Add(sorted[index]);
            }
            return result;
        }

        private MatchEntity CreateMatch(
            Guid tournamentId,
            Guid stageId,
            int round,
            MatchStage stage,
            int order)
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
    }
}