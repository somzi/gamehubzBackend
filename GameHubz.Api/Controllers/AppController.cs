using GameHubz.DataModels.Config;
using GameHubz.DataModels.Consts;
using GameHubz.DataModels.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GameHubz.Api.Controllers
{
    /// <summary>
    /// What the app needs to know about itself before anything else — today, whether this build is still
    /// supported. A new surface with no legacy callers.
    /// <para>
    /// Anonymous on purpose: the check runs on the sign-in screen too, and an outdated build should be
    /// stopped before anyone signs in with it.
    /// </para>
    /// </summary>
    [Route("api/app")]
    [ApiController]
    [AllowAnonymous]
    public class AppController : ControllerBase
    {
        private readonly ShareLinksConfig shareLinksConfig;

        public AppController(IOptions<ShareLinksConfig> shareLinksOptions)
        {
            this.shareLinksConfig = shareLinksOptions.Value;
        }

        /// <summary>
        /// The minimum supported app version, plus the store pages the update screen links to — taken from
        /// the same ShareLinksConfig the share pages use, so a store URL is configured in one place.
        /// </summary>
        [HttpGet("version-check")]
        public ActionResult<AppVersionCheckDto> VersionCheck()
        {
            return Ok(new AppVersionCheckDto
            {
                MinSupportedVersion = AppVersionRules.MinSupportedAppVersion,
                IosStoreUrl = NullIfBlank(shareLinksConfig.AppStoreUrl),
                AndroidStoreUrl = NullIfBlank(shareLinksConfig.PlayStoreUrl),
            });
        }

        private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
