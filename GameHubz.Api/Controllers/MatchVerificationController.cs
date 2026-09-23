using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    /// <summary>
    /// "Verify Result" — see <see cref="MatchVerificationService"/> for the flow and what each step
    /// proves. Lives under api/match so the routes read as part of the match they verify. Every rule
    /// (who may verify, in which order, inside which window) is the service's.
    /// </summary>
    [Route("api/match")]
    [ApiController]
    [Authorize]
    public class MatchVerificationController : ControllerBase
    {
        private readonly MatchVerificationService verificationService;

        public MatchVerificationController(MatchVerificationService verificationService)
        {
            this.verificationService = verificationService;
        }

        /// <summary>Registers the caller's phone (or re-issues its key). The key is in this response only.</summary>
        [HttpPost("verification/device")]
        public async Task<IActionResult> RegisterDevice([FromBody] RegisterVerificationDeviceRequest request)
        {
            return Ok(await this.verificationService.RegisterDevice(request));
        }

        /// <summary>The verification state of a match, shaped for the caller (organizer / player / spectator).</summary>
        [HttpGet("{id}/verification")]
        public async Task<IActionResult> GetPanel(Guid id)
        {
            return Ok(await this.verificationService.GetPanel(id));
        }

        /// <summary>
        /// Issues a single-use challenge for one attempt. 409 when the phone's installation id has no
        /// key on this account — the phone's cue to register again and retry, which it cannot take from
        /// a 400 that might equally mean "this tournament does not verify".
        /// </summary>
        [HttpPost("{id}/verification/start")]
        public async Task<IActionResult> Start(Guid id, [FromBody] StartMatchVerificationRequest request)
        {
            try
            {
                return Ok(await this.verificationService.Start(id, request));
            }
            catch (VerificationDeviceUnknownException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        /// <summary>The challenge, signed with the device's biometric-locked key.</summary>
        [HttpPost("verification/{verificationId}/biometric")]
        public async Task<IActionResult> SubmitBiometricProof(Guid verificationId, [FromBody] SubmitBiometricProofRequest request)
        {
            try
            {
                return Ok(await this.verificationService.SubmitBiometricProof(verificationId, request));
            }
            catch (VerificationDeviceUnknownException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        // One clip, already compressed on the phone: the per-file cap in the storage layer is 32MB, so
        // this envelope leaves room for the multipart framing and the metadata fields around it and no
        // more — ordinary evidence keeps the only larger limit in the API.
        [RequestSizeLimit(40 * 1024 * 1024)]
        [HttpPost("verification/{verificationId}/evidence")]
        public async Task<IActionResult> AttachEvidence(
            Guid verificationId,
            IFormFile? file,
            [FromForm] AttachVerificationEvidenceRequest meta)
        {
            return Ok(await this.verificationService.AttachEvidence(verificationId, file, meta));
        }
    }
}
