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

        [HttpGet("{id}/matches")]
        public async Task<List<MatchListItemDto>> GetMatches(Guid id, int pageNumber)
        {
            var matches = await userProfileService.GetMatches(id, pageNumber);

            return matches;
        }

        [HttpGet("{id}/tournaments")]
        public async Task<EntityListDto<TournamentOverview>> GetTournaments(Guid id, [FromQuery] int pageNumber)
        {
            var userProfile = await userProfileService.GetTournaments(id, pageNumber);

            return userProfile;
        }

        [HttpPost("avatar")]
        public async Task<IActionResult> UploadAvatar(IFormFile avatar)
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