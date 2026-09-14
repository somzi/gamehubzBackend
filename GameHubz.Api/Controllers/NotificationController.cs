using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    /// <summary>
    /// The signed-in user's notification inbox. New surface (no legacy clients), so it is versioned at
    /// v2 from the start, like BadgeController. Every write returns the fresh counters.
    /// </summary>
    [Route("api/v2/notifications")]
    [ApiController]
    [Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly NotificationInboxService inboxService;

        public NotificationController(NotificationInboxService inboxService)
        {
            this.inboxService = inboxService;
        }

        /// <summary>
        /// Newest first. <paramref name="category"/>: "action" or "update" (omit for everything).
        /// <paramref name="before"/>: the previous page's nextCursor.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPage([FromQuery] string? category, [FromQuery] int? take, [FromQuery] string? before)
        {
            return Ok(await this.inboxService.GetPage(category, take, before));
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            return Ok(await this.inboxService.GetSummary());
        }

        /// <summary>The user opened the inbox — clears the bell badge.</summary>
        [HttpPost("seen")]
        public async Task<IActionResult> MarkSeen()
        {
            return Ok(await this.inboxService.MarkSeen());
        }

        [HttpPost("{id:guid}/read")]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            return Ok(await this.inboxService.MarkRead(id));
        }

        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllRead([FromQuery] string? category)
        {
            return Ok(await this.inboxService.MarkAllRead(category));
        }
    }
}
