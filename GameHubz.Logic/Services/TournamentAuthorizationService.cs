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
        public TournamentAuthorizationService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
        }

        public async Task<bool> CanManageTournamentAsync(Guid tournamentId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContext();
            if (user == null) return false;

            return await this.CanManageTournamentAsync(tournamentId, user);
        }

        public async Task<bool> CanManageTournamentAsync(Guid tournamentId, TokenUserInfo user)
        {
            if (user.RoleEnum == UserRoleEnum.Admin) return true;

            var ownership = await this.AppUnitOfWork.TournamentRepository.GetHubOwnership(tournamentId);
            if (ownership == null) return false;

            if (user.UserId == ownership.OwnerUserId) return true;

            var role = await this.AppUnitOfWork.UserHubRepository.GetRole(user.UserId, ownership.HubId);
            return role == HubRole.HubOwner || role == HubRole.HubAdmin;
        }
    }
}
