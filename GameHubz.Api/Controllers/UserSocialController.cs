using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/")]
    [ApiController]
    public class UserSocialController : BasicGenericController<UserSocialService, UserSocialEntity, UserSocialDto, UserSocialPost, UserSocialEdit>
    {
        public UserSocialController(
            UserSocialService service,
            AppAuthorizationService appAuthorizationService)
            : base(service, appAuthorizationService)
        {
        }
    }
}