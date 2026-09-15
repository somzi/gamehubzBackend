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

        // Membership removal must use the explicit self-unfollow or manager removal flows, both of
        // which derive the caller from the token and enforce hub-role rules.
        [NonAction]
        public override Task Delete(Guid id)
            => throw new NotSupportedException("Use the explicit hub membership endpoints.");

        [HttpDelete("unfollow")]
        public async Task Unfollow([FromQuery] Guid userId, [FromQuery] Guid hubId)
        {
            //await this.AppAuthorizationService.CheckAuthorization(this.UserRolesDelete());

            await this.Service.Unfollow(userId, hubId);

            this.Ok();
        }
    }
}
