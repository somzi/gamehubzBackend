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

        [HttpGet("v2/{id}/stats")]
        public async Task<PlayerMatchesV2Dto> GetStatsV2(Guid id)
        {
            var stats = await userProfileService.GetStatsV2(id);

            return stats;
        }

        [HttpGet("{id}/head-to-head/{opponentId}")]
        public async Task<HeadToHeadDto> GetHeadToHead(Guid id, Guid opponentId)
        {
            return await userProfileService.GetHeadToHead(id, opponentId);
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

        /// <summary>
        /// Personal notification switches and excluded hubs/tournaments.
        /// </summary>
        [HttpGet("notification-settings")]
        public async Task<IActionResult> GetNotificationSettings()
        {
            return Ok(await userProfileService.GetMyNotificationSettings());
        }

        [HttpPut("notification-settings")]
        public async Task<IActionResult> UpdateNotificationSettings([FromBody] NotificationSettingsDto settings)
        {
            return Ok(await userProfileService.UpdateMyNotificationSettings(settings));
        }

        [HttpGet("notification-sources")]
        public async Task<IActionResult> GetNotificationSources([FromQuery] string kind = "hubs", [FromQuery] int page = 0, [FromQuery] string? search = null)
        {
            if ((kind != "hubs" && kind != "tournaments") || page < 0 || page > 100000 || search?.Length > 200)
                return BadRequest();
            return Ok(await userProfileService.GetNotificationSources(kind, page, search));
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
