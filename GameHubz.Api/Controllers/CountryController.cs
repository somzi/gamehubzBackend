using GameHubz.DataModels.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CountryController : ControllerBase
    {
        /// <summary>
        /// Returns the full selectable country catalog (ISO code, display name, flag emoji, and the
        /// GameHubz region the country belongs to). Anonymous because the registration screen needs
        /// it before the user has a token. Region is the numeric RegionType, matching the client enum.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public IActionResult GetAll()
        {
            var countries = CountryCatalog.All
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    c.Code,
                    c.Name,
                    c.Flag,
                    Region = (int)c.Region
                });

            return Ok(countries);
        }
    }
}