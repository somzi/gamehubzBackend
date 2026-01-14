using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserProfileController : ControllerBase
    {
        private readonly UserProfileService userProfileService;

        public UserProfileController(UserProfileService userProfileService)
        {
            this.userProfileService = userProfileService;
        }

        [HttpGet("{id}/info")]
        public async Task<UserProfileDto> GetProfile(Guid id)
        {
            var userProfile = await userProfileService.GetUserProfileAsync(id);

            return userProfile;
        }

        [HttpGet("{id}/stats")]
        public async Task<PlayerMatchesDto> GetStats(Guid id)
        {
            var userProfile = await userProfileService.GetStats(id);

            return userProfile;
        }
    }
}