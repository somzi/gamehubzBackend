using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    /// <summary>
    /// Notification badge counters for the signed-in user. New surface (no legacy
    /// clients), so it is versioned at v2 from the start.
    /// </summary>
    [Route("api/v2/badges")]
    [ApiController]
    [Authorize]
    public class BadgeController : ControllerBase
    {
        private readonly BadgeService badgeService;

        public BadgeController(BadgeService badgeService)
        {
            this.badgeService = badgeService;
        }

        [HttpGet]
        public async Task<IActionResult> GetBadges()
        {
            return Ok(await this.badgeService.GetMyBadgesAsync());
        }
    }
}
