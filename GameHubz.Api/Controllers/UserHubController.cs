using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/userHub")]
    [ApiController]
    [Authorize]
    public class UserHubController : BasicGenericController<UserHubService, UserHubEntity, UserHubDto, UserHubPost, UserHubEdit>
    {
        public UserHubController(
            UserHubService service,
            AppAuthorizationService appAuthorizationService)
            : base(service, appAuthorizationService)
        {
        }

        [HttpDelete("unfollow")]
        public async Task Unfollow([FromQuery] Guid userId, [FromQuery] Guid hubId)
        {
            //await this.AppAuthorizationService.CheckAuthorization(this.UserRolesDelete());

            await this.Service.Unfollow(userId, hubId);

            this.Ok();
        }
    }
}