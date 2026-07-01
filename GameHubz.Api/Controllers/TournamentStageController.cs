using GameHubz.Common.Consts;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TournamentStageController : BasicGenericController<TournamentStageService, TournamentStageEntity, TournamentStageDto, TournamentStagePost, TournamentStageEdit>
    {
        public TournamentStageController(
            TournamentStageService service,
            AppAuthorizationService appAuthorizationService)
            : base(service, appAuthorizationService)
        {
        }

        // F106: the inherited generic POST/DELETE let any authenticated user create/edit/delete the
        // stages of any tournament (tampering with bracket structure). Tournament stages are created by
        // the manager-authorized bracket generation flow, so lock the generic write paths to Admin.
        protected override UserRoleEnum[]? UserRolesSave() => [UserRoleEnum.Admin];

        protected override UserRoleEnum[]? UserRolesDelete() => [UserRoleEnum.Admin];
    }
}