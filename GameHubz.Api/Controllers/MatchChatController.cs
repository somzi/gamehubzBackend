using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class MatchChatController : BasicGenericController<MatchChatService, MatchChatEntity, MatchChatDto, MatchChatPost, MatchChatEdit>
    {
        private readonly MatchChatService chatService;

        public MatchChatController(
            MatchChatService service,
            AppAuthorizationService appAuthorizationService,
            MatchChatService chatService)
            : base(service, appAuthorizationService)
        {
            this.chatService = chatService;
        }

        [HttpGet("{matchId}/history")]
        public async Task<IActionResult> GetHistory(Guid matchId, [FromQuery] int? take = null, [FromQuery] DateTime? before = null)
        {
            var history = await chatService.GetHistory(matchId, take, before);
            return Ok(history);
        }

        [HttpPost("{matchId}")]
        public async Task<IActionResult> SendMessage(Guid matchId, [FromBody] CreateMessageDto body)
        {
            var result = await chatService.SendMessage(matchId, body.Content);
            return Ok(result);
        }

        [HttpGet("{matchId}/mute")]
        public async Task<IActionResult> GetMuted(Guid matchId)
        {
            return Ok(new { muted = await chatService.GetMuted(matchId) });
        }

        /// <summary>
        /// Mute / unmute this match's chat for the caller — no push, no Discord DM, no badge, while
        /// the thread stays in the inbox with its real unread count.
        /// </summary>
        [HttpPut("{matchId}/mute")]
        public async Task<IActionResult> SetMuted(Guid matchId, [FromBody] SetMatchChatMutedRequest body)
        {
            await chatService.SetMuted(matchId, body.Muted);
            return NoContent();
        }

        [HttpPost("{matchId}/read")]
        public async Task<IActionResult> MarkRead(Guid matchId)
        {
            await chatService.MarkRead(matchId);
            return NoContent();
        }
    }
}
