using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DirectChatController : ControllerBase
    {
        private readonly DirectChatService chatService;

        public DirectChatController(DirectChatService chatService)
        {
            this.chatService = chatService;
        }

        [HttpGet]
        public async Task<IActionResult> GetMyChats([FromQuery] string? search)
        {
            return Ok(await chatService.GetMyChats(search));
        }

        [HttpPost("with/{otherUserId}")]
        public async Task<IActionResult> GetOrCreate(Guid otherUserId)
        {
            return Ok(await chatService.GetOrCreateChat(otherUserId));
        }

        [HttpGet("{chatId}/messages")]
        public async Task<IActionResult> GetMessages(Guid chatId, [FromQuery] int take = 100, [FromQuery] DateTime? before = null)
        {
            return Ok(await chatService.GetMessages(chatId, take, before));
        }

        [HttpPost("{chatId}/messages")]
        public async Task<IActionResult> SendMessage(Guid chatId, [FromBody] SendDirectMessageDto body)
        {
            return Ok(await chatService.SendMessage(chatId, body.Content));
        }

        [HttpPost("{chatId}/read")]
        public async Task<IActionResult> MarkRead(Guid chatId)
        {
            await chatService.MarkRead(chatId);
            return NoContent();
        }
    }
}
