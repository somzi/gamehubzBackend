using FluentValidation;
using GameHubz.DataModels.Enums;
using Microsoft.AspNetCore.Http;

namespace GameHubz.Logic.Services
{
    public class MatchService : AppBaseServiceGeneric<MatchEntity, MatchDto, MatchPost, MatchEdit>
    {
        private readonly CloudinaryStorageService storageService;
        private readonly INotificationService notificationService;

        public MatchService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<MatchEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            CloudinaryStorageService storageService,
            INotificationService notificationService) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.storageService = storageService;
            this.notificationService = notificationService;
        }

        public async Task<MatchAvailabilityDto> GetAvailability(Guid id, Guid userId)
        {
            return await this.AppUnitOfWork.MatchRepository.GetAvailability(id, userId);
        }

        public async Task<List<MatchOverviewDto>> GetByUser(Guid userId)
        {
            return await this.AppUnitOfWork.MatchRepository.GetByUser(userId);
        }

        public async Task<MatchResultDetailDto> GetWithEvidence(Guid id)
        {
            return await this.AppUnitOfWork.MatchRepository.GetWithEvidence(id);
        }

        public async Task<MatchEntity?> GetMatchEntityById(Guid id)
        {
            return await this.AppUnitOfWork.MatchRepository.ShallowGetById(id);
        }

        public async Task<MatchAvailabilityDto> SetAvailability(Guid matchId, List<DateTime> selectedSlots)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var userId = user.UserId;
            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new Exception("Match not found");

            // 1. Determine side (Home vs Away)
            bool isHome = match.HomeParticipant != null &&
                (match.HomeParticipant.UserId == userId ||
                 match.HomeParticipant.Team?.Members.Any(m => m.UserId == userId) == true);
            bool isAway = match.AwayParticipant != null &&
                (match.AwayParticipant.UserId == userId ||
                 match.AwayParticipant.Team?.Members.Any(m => m.UserId == userId) == true);

            //if (!isHome && !isAway) throw new Exception("User is not a participant in this match");

            // 2. Save Slots — normalize to UTC so Intersect() uses consistent DateTimeKind
            var normalizedSlots = selectedSlots
                .Select(s => DateTime.SpecifyKind(s, DateTimeKind.Utc))
                .ToList();

            if (isHome)
            {
                match.HomeSlots = normalizedSlots;
            }
            else
            {
                match.AwaySlots = normalizedSlots;
            }

            // 3. CHECK FOR OVERLAP (The Magic)
            // We check if the other person has already picked times
            var mySlots = isHome ? match.HomeSlots : match.AwaySlots;
            var opponentSlots = isHome ? match.AwaySlots : match.HomeSlots;

            // Find slots present in BOTH lists
            var intersection = mySlots.Intersect(opponentSlots).ToList();

            if (intersection.Count > 0)
            {
                // OrderBy ensures we pick the EARLIEST mutual time (e.g. 10:00 instead of 14:00)
                match.ScheduledStartTime = intersection.OrderBy(t => t).First();
                match.Status = MatchStatus.Scheduled;
            }

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();

            SendNotification(matchId, user, match, isHome);

            // 4. Return DTO for UI
            return new MatchAvailabilityDto
            {
                MatchId = match.Id!.Value,
                MySlots = mySlots,
                OpponentSlots = opponentSlots,
                ConfirmedTime = match.ScheduledStartTime,
                MatchDeadline = match.RoundDeadline
            };
        }

        private void SendNotification(Guid matchId, TokenUserInfo user, MatchEntity match, bool isHome)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (match.Status == MatchStatus.Scheduled)
                    {
                        var homeUserId = GetParticipantUserId(match, isHome: true);
                        var awayUserId = GetParticipantUserId(match, isHome: false);

                        Guid? opponentUserId = isHome
                            ? GetParticipantUserId(match, isHome: false)
                            : GetParticipantUserId(match, isHome: true);

                        if (opponentUserId == null) return;

                        var opponent = await this.AppUnitOfWork.UserRepository.GetById(opponentUserId.Value);

                        if (opponent == null) return;

                        string body = $"Your match is confirmed vs {user.Username}";

                        if (opponent?.PushToken == null) return;

                        await notificationService.SendToOneAsync(opponent.PushToken, "Match Scheduled", body, new { matchId = matchId.ToString() });
                    }
                    else
                    {
                        Guid? opponentUserId = isHome
                            ? GetParticipantUserId(match, isHome: false)
                            : GetParticipantUserId(match, isHome: true);
                        if (opponentUserId == null) return;

                        var opponent = await this.AppUnitOfWork.UserRepository.GetById(opponentUserId.Value);
                        if (opponent?.PushToken == null) return;

                        await notificationService.SendToOneAsync(
                            opponent.PushToken,
                            "Match schedule",
                            $"{user.Username} set their availability, add yours to confirm a time",
                            new { matchId = matchId.ToString() });
                    }
                }
                catch { /* fire-and-forget */ }
            });
        }

        public async Task UploadMatchEvidence(Guid matchId, List<IFormFile> files)
        {
            var match = await this.AppUnitOfWork.MatchRepository.GetForMatchEvidence(matchId);
            if (match == null) throw new Exception("Match not found");

            foreach (var file in files)
            {
                if (file.Length > 0)
                {
                    string fileName = $"evidence_{matchId}_{DateTime.UtcNow.Ticks}";
                    string folderPath = $"hub/{match.HubName}/tournaments/{match.TournamentName}/matches/{matchId}";

                    string url = await storageService.UploadFileAsync(file, folderPath, fileName);

                    var screenshot = new MatchEvidenceEntity
                    {
                        MatchId = matchId,
                        Url = url,
                    };

                    await this.AppUnitOfWork.MatchEvidenceRepository.AddEntity(screenshot, this.UserContextReader);
                }
            }

            await this.SaveAsync();

            // 4. Obriši keš (jer se meč promenio)
            // await _cacheService.RemoveAsync($"match:{matchId}");
        }

        private static Guid? GetParticipantUserId(MatchEntity match, bool isHome) =>
            isHome
                ? (match.HomeUserId ?? match.HomeParticipant?.UserId)
                : (match.AwayUserId ?? match.AwayParticipant?.UserId);

        protected override IRepository<MatchEntity> GetRepository()
            => this.AppUnitOfWork.MatchRepository;
    }
}

//