using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class FriendController : ControllerBase
    {
        private readonly FriendService friendService;

        public FriendController(FriendService friendService)
        {
            this.friendService = friendService;
        }

        [HttpGet]
        public async Task<IActionResult> GetMyFriends([FromQuery] string? search)
        {
            return Ok(await friendService.GetMyFriends(search));
        }

        [HttpGet("requests/incoming")]
        public async Task<IActionResult> GetIncomingRequests([FromQuery] string? search)
        {
            return Ok(await friendService.GetIncomingRequests(search));
        }

        [HttpGet("requests/outgoing")]
        public async Task<IActionResult> GetOutgoingRequests([FromQuery] string? search)
        {
            return Ok(await friendService.GetOutgoingRequests(search));
        }

        [HttpGet("blocked")]
        public async Task<IActionResult> GetBlocked([FromQuery] string? search)
        {
            return Ok(await friendService.GetBlocked(search));
        }

        [HttpGet("status/{otherUserId}")]
        public async Task<IActionResult> GetStatus(Guid otherUserId)
        {
            return Ok(await friendService.GetRelationStatus(otherUserId));
        }

        public class TargetUserBody
        {
            public Guid UserId { get; set; }
        }

        [HttpPost("request")]
        public async Task<IActionResult> SendRequest([FromBody] TargetUserBody body)
        {
            return Ok(await friendService.SendRequest(body.UserId));
        }

        [HttpPost("requests/{requestId}/accept")]
        public async Task<IActionResult> AcceptRequest(Guid requestId)
        {
            await friendService.AcceptRequest(requestId);
            return NoContent();
        }

        [HttpPost("requests/{requestId}/reject")]
        public async Task<IActionResult> RejectRequest(Guid requestId)
        {
            await friendService.RejectRequest(requestId);
            return NoContent();
        }

        [HttpPost("requests/{requestId}/cancel")]
        public async Task<IActionResult> CancelRequest(Guid requestId)
        {
            await friendService.CancelRequest(requestId);
            return NoContent();
        }

        [HttpDelete("{otherUserId}")]
        public async Task<IActionResult> Unfriend(Guid otherUserId)
        {
            await friendService.Unfriend(otherUserId);
            return NoContent();
        }

        [HttpPost("block")]
        public async Task<IActionResult> Block([FromBody] TargetUserBody body)
        {
            await friendService.Block(body.UserId);
            return NoContent();
        }

        [HttpDelete("block/{otherUserId}")]
        public async Task<IActionResult> Unblock(Guid otherUserId)
        {
            await friendService.Unblock(otherUserId);
            return NoContent();
        }
    }
}
