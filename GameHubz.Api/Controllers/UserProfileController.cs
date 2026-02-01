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

        [HttpGet("{id}/tournaments")]
        public async Task<List<TournamentOverview>> GetTournaments(Guid id)
        {
            var userProfile = await userProfileService.GetTournaments(id);

            return userProfile;
        }

        [HttpPost("avatar")]
        public async Task<IActionResult> UploadAvatar([FromForm] IFormFile avatar)
        {
            if (avatar == null || avatar.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }
            await userProfileService.UploadAvatar(avatar);

            return Ok("Avatar uploaded successfully.");
        }
    }
}