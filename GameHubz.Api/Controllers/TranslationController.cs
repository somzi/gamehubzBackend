using DeepL;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TranslationController : ControllerBase
    {
        private readonly TranslationService translationService;
        private readonly ILogger<TranslationController> logger;

        public TranslationController(
            TranslationService translationService,
            ILogger<TranslationController> logger)
        {
            this.translationService = translationService;
            this.logger = logger;
        }

        [HttpPost("translate")]
        public async Task<IActionResult> Translate([FromBody] TranslateMessageDto body)
        {
            if (body is null
                || string.IsNullOrWhiteSpace(body.Text)
                || string.IsNullOrWhiteSpace(body.TargetLanguage))
            {
                return BadRequest(new { error = "Text and TargetLanguage are required." });
            }

            try
            {
                var result = await this.translationService.TranslateAsync(body);
                return Ok(result);
            }
            catch (AuthorizationException ex)
            {
                this.logger.LogError(ex, "DeepL authorization failed.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "Translation service authorization failed." });
            }
            catch (QuotaExceededException ex)
            {
                this.logger.LogWarning(ex, "DeepL quota exceeded.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "Translation quota exceeded. Please try again later." });
            }
            catch (ConnectionException ex)
            {
                this.logger.LogError(ex, "DeepL connection error.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "Could not reach the translation service." });
            }
            catch (DeepLException ex)
            {
                this.logger.LogError(ex, "DeepL API error.");
                return BadRequest(new { error = $"Translation failed: {ex.Message}" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Unexpected error during translation.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "An unexpected error occurred while translating." });
            }
        }
    }
}
