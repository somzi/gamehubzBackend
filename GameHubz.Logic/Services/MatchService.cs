using FluentValidation;
using GameHubz.DataModels.Enums;
using Microsoft.AspNetCore.Http;

namespace GameHubz.Logic.Services
{
    public class MatchService : AppBaseServiceGeneric<MatchEntity, MatchDto, MatchPost, MatchEdit>
    {
        private readonly CloudinaryStorageService storageService;
        private readonly INotificationService notificationService;
        private readonly TournamentAuthorizationService tournamentAuth;

        public MatchService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<MatchEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            CloudinaryStorageService storageService,
            INotificationService notificationService,
            TournamentAuthorizationService tournamentAuth) : base(
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
            this.tournamentAuth = tournamentAuth;
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

        public async Task SetScheduled(Guid matchId)
        {
            var match = await this.AppUnitOfWork.MatchRepository.ShallowGetById(matchId);
            if (match == null) throw new Exception("Match not found");

            match.ScheduledStartTime = DateTime.UtcNow;
            match.Status = MatchStatus.Scheduled;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();
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

        public async Task RequestAdminHelp(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new Exception("Match not found");

            if (!IsMatchParticipant(match, user.UserId))
                throw new Exception("Only match participants can request admin help");

            // Idempotent: a second tap must not spam the admins with notifications.
            if (match.AdminHelpRequested) return;

            match.AdminHelpRequested = true;
            match.AdminHelpRequestedByUserId = user.UserId;
            match.AdminHelpRequestedOn = DateTime.UtcNow;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();

            NotifyAdminsHelpRequested(match, user);
        }

        public async Task ResolveAdminHelp(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.ShallowGetById(matchId);
            if (match == null) throw new Exception("Match not found");

            if (!await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, user))
                throw new Exception("Only tournament admins can resolve help requests");

            if (!match.AdminHelpRequested) return;

            var requesterUserId = match.AdminHelpRequestedByUserId;
            match.AdminHelpRequested = false;
            match.AdminHelpRequestedByUserId = null;
            match.AdminHelpRequestedOn = null;

            await this.AppUnitOfWork.MatchRepository.UpdateEntity(match, this.UserContextReader);
            await this.SaveAsync();

            NotifyHelpResolved(matchId, requesterUserId);
        }

        public async Task<List<MatchAdminHelpItemDto>> GetAdminHelpRequests(Guid tournamentId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (!await this.tournamentAuth.CanManageTournamentAsync(tournamentId, user))
                throw new Exception("Only tournament admins can view help requests");

            return await this.AppUnitOfWork.MatchRepository.GetAdminHelpRequests(tournamentId);
        }

        private static bool IsMatchParticipant(MatchEntity match, Guid userId)
        {
            // Team sub-matches carry the player ids on the match itself; solo matches use the participants.
            if (match.HomeUserId == userId || match.AwayUserId == userId) return true;

            return (match.HomeParticipant != null &&
                        (match.HomeParticipant.UserId == userId ||
                         match.HomeParticipant.Team?.Members.Any(m => m.UserId == userId) == true)) ||
                   (match.AwayParticipant != null &&
                        (match.AwayParticipant.UserId == userId ||
                         match.AwayParticipant.Team?.Members.Any(m => m.UserId == userId) == true));
        }

        private void NotifyAdminsHelpRequested(MatchEntity match, TokenUserInfo requester)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var ownership = await this.AppUnitOfWork.TournamentRepository.GetHubOwnership(match.TournamentId);
                    if (ownership == null) return;

                    var tournament = await this.AppUnitOfWork.TournamentRepository.GetById(match.TournamentId);

                    var hubUsers = await this.AppUnitOfWork.UserHubRepository.GetUsersByHub(ownership.HubId);
                    var pushTokens = hubUsers
                        .Where(m => (m.HubRole == HubRole.HubOwner || m.HubRole == HubRole.HubAdmin)
                                    && m.UserId != requester.UserId
                                    && !string.IsNullOrEmpty(m.PushToken))
                        .Select(m => m.PushToken!)
                        .ToList();

                    // The hub owner may not have a UserHub membership row — include them explicitly.
                    if (ownership.OwnerUserId != requester.UserId &&
                        !hubUsers.Any(m => m.UserId == ownership.OwnerUserId))
                    {
                        var owner = await this.AppUnitOfWork.UserRepository.GetById(ownership.OwnerUserId);
                        if (!string.IsNullOrEmpty(owner?.PushToken)) pushTokens.Add(owner!.PushToken!);
                    }

                    if (pushTokens.Count == 0) return;

                    await notificationService.SendToManyAsync(
                        pushTokens.Distinct(),
                        tournament?.Name ?? "Admin help needed",
                        $"{requester.Username} requested admin help in their match.",
                        new
                        {
                            matchId = match.Id!.Value.ToString(),
                            tournamentId = match.TournamentId.ToString(),
                            type = "adminHelp"
                        });
                }
                catch { /* fire-and-forget */ }
            });
        }

        private void NotifyHelpResolved(Guid matchId, Guid? requesterUserId)
        {
            if (requesterUserId == null) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var requester = await this.AppUnitOfWork.UserRepository.GetById(requesterUserId.Value);
                    if (requester?.PushToken == null) return;

                    await notificationService.SendToOneAsync(
                        requester.PushToken,
                        "Help request resolved",
                        "An admin reviewed your match and marked the issue as resolved.",
                        new { matchId = matchId.ToString(), type = "adminHelpResolved" });
                }
                catch { /* fire-and-forget */ }
            });
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