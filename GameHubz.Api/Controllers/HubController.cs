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
    public class HubController : BasicGenericController<HubService, HubEntity, HubDto, HubPost, HubEdit>
    {
        public HubController(
            HubService service,
            AppAuthorizationService appAuthorizationService)
            : base(service, appAuthorizationService)
        {
        }

        protected override UserRoleEnum[]? UserRolesDelete() => new[] { UserRoleEnum.Admin };

        protected override UserRoleEnum[]? UserRolesRead() => new[] { UserRoleEnum.Admin };
    }
}