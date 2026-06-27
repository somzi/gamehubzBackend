using GameHubz.Common.Consts;
using GameHubz.Common.Models;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Resolves whether a caller may perform owner-level actions on a tournament.
    /// A tournament manager is the platform admin, the hub owner of the hub that owns the
    /// tournament, or a hub admin of that same hub.
    /// </summary>
    public class TournamentAuthorizationService : AppBaseService
    {
        private readonly ICacheService cacheService;
        private readonly UserHubService userHubService;

        // Longer TTL is safe now: tournament_authz:{userId}:* is explicitly invalidated by
        // UserHubService.InvalidateHubCaches and HubService.KickUserFromHub on every role
        // change. Falls back to TTL only in the rare case an invalidation is missed.
        private static readonly TimeSpan AuthzTtl = TimeSpan.FromMinutes(10);

        public TournamentAuthorizationService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            ICacheService cacheService,
            UserHubService userHubService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.cacheService = cacheService;
            this.userHubService = userHubService;
        }

        public async Task<bool> CanManageTournamentAsync(Guid tournamentId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContext();
            if (user == null) return false;

            return await this.CanManageTournamentAsync(tournamentId, user);
        }

        public async Task<bool> CanManageTournamentAsync(Guid tournamentId, TokenUserInfo user)
        {
            // Platform admin short-circuit — never cache this branch since it depends only on
            // the JWT claim, which is read-cheap.
            if (user.RoleEnum == UserRoleEnum.Admin) return true;

            string cacheKey = $"tournament_authz:{user.UserId}:{tournamentId}";
            var cached = await this.cacheService.GetAsync<bool?>(cacheKey);
            if (cached.HasValue) return cached.Value;

            var ownership = await this.AppUnitOfWork.TournamentRepository.GetHubOwnership(tournamentId);
            if (ownership == null) return false;

            bool result;
            if (user.UserId == ownership.OwnerUserId)
            {
                result = true;
            }
            else
            {
                var role = await this.userHubService.GetUserHubRoleCachedAsync(user.UserId, ownership.HubId);
                result = role == HubRole.HubOwner || role == HubRole.HubAdmin;
            }

            await this.cacheService.SetAsync<bool?>(cacheKey, result, AuthzTtl);
            return result;
        }
    }
}
