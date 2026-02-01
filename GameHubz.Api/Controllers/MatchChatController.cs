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
        public async Task<IActionResult> GetHistory(Guid matchId)
        {
            var history = await chatService.GetHistory(matchId);
            return Ok(history);
        }

        [HttpPost("{matchId}")]
        public async Task<IActionResult> SendMessage(Guid matchId, [FromBody] CreateMessageDto body)
        {
            var result = await chatService.SendMessage(matchId, body.Content);
            return Ok(result);
        }
    }
}