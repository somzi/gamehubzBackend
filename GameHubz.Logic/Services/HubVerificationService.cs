using GameHubz.Common.Consts;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    public class HubVerificationService : AppBaseService
    {
        private const string VerificationReviewEmail = "todorovic.misa@hotmail.com";

        private readonly IMapper mapper;
        private readonly AppAuthorizationService appAuthorizationService;
        private readonly EmailService emailService;
        private readonly ICacheService cacheService;

        public HubVerificationService(
            IUnitOfWorkFactory factory,
            ILocalizationService localizationService,
            IUserContextReader userContextReader,
            IMapper mapper,
            AppAuthorizationService appAuthorizationService,
            EmailService emailService,
            ICacheService cacheService)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.mapper = mapper;
            this.appAuthorizationService = appAuthorizationService;
            this.emailService = emailService;
            this.cacheService = cacheService;
        }

        public async Task<HubVerificationRequestDto> RequestVerification(Guid hubId, string reason)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(hubId);

            if (hub.UserId != user.UserId)
                throw new Exception("Only the hub owner can request verification.");

            if (hub.IsVerified)
                throw new Exception("This hub is already verified.");

            if (string.IsNullOrWhiteSpace(reason))
                throw new Exception("Please provide a reason and evidence for the verification request.");

            var pending = await this.AppUnitOfWork.HubVerificationRequestRepository.GetPendingForHub(hubId);
            if (pending != null)
                throw new Exception("A verification request for this hub is already pending review.");

            var request = new HubVerificationRequestEntity
            {
                HubId = hubId,
                Reason = reason.Trim(),
                Status = HubVerificationStatus.Pending
            };

            await this.AppUnitOfWork.HubVerificationRequestRepository.AddEntity(request, this.UserContextReader);
            await this.SaveAsync();

            await this.SendReviewEmail(hub, request, user.UserId);

            return this.mapper.Map<HubVerificationRequestDto>(request);
        }

        public async Task<HubVerificationRequestDto> RespondVerification(Guid hubId, bool approved)
        {
            await this.appAuthorizationService.CheckAuthorization(new[] { UserRoleEnum.Admin });

            var request = await this.AppUnitOfWork.HubVerificationRequestRepository.GetPendingForHub(hubId)
                ?? throw new Exception("No pending verification request found for this hub.");

            request.Status = approved ? HubVerificationStatus.Approved : HubVerificationStatus.Rejected;
            await this.AppUnitOfWork.HubVerificationRequestRepository.UpdateEntity(request, this.UserContextReader);

            if (approved)
            {
                var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(hubId);
                hub.IsVerified = true;
                await this.AppUnitOfWork.HubRepository.UpdateEntity(hub, this.UserContextReader);
            }

            await this.SaveAsync();

            await this.cacheService.RemoveAsync($"hub_overview:{hubId}");
            await this.cacheService.RemoveAsync("hubs_overview_all");

            return this.mapper.Map<HubVerificationRequestDto>(request);
        }

        public async Task<HubVerificationRequestDto?> GetCurrentForHub(Guid hubId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var hub = await this.AppUnitOfWork.HubRepository.GetByIdOrThrowIfNull(hubId);
            if (hub.UserId != user.UserId)
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);

            var latest = await this.AppUnitOfWork.HubVerificationRequestRepository.GetLatestForHub(hubId);
            return latest == null ? null : this.mapper.Map<HubVerificationRequestDto>(latest);
        }

        private async Task SendReviewEmail(HubEntity hub, HubVerificationRequestEntity request, Guid requestingUserId)
        {
            var owner = await this.AppUnitOfWork.UserRepository.GetById(requestingUserId);
            var ownerName = owner?.Username ?? requestingUserId.ToString();

            var subject = $"[Hub verification] {hub.Name}";
            var body = $@"
                <h2>New Hub Verification Request</h2>
                <p><strong>Hub:</strong> {System.Net.WebUtility.HtmlEncode(hub.Name)}</p>
                <p><strong>Hub ID:</strong> {hub.Id}</p>
                <p><strong>Requested by:</strong> {System.Net.WebUtility.HtmlEncode(ownerName)} ({requestingUserId})</p>
                <p><strong>Requested at:</strong> {request.CreatedOn:yyyy-MM-dd HH:mm} UTC</p>
                <h3>Reason / Evidence</h3>
                <p style=""white-space: pre-wrap;"">{System.Net.WebUtility.HtmlEncode(request.Reason)}</p>
            ";

            var emailModel = new EmailModel
            {
                To = VerificationReviewEmail,
                Subject = subject,
                Message = body,
                IsMessageHtml = true,
                Cc = string.Empty
            };

            try
            {
                await this.emailService.SendEmail(emailModel);
            }
            catch
            {
                // Email failure should not roll back the request itself —
                // the row is the durable record reviewers will fall back to.
            }
        }
    }
}
