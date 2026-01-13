using FluentMigrator.Runner;
using Microsoft.AspNetCore.Mvc;
using GameHubz.Api.Common.Filters;

namespace GameHubz.DataMigrations.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [BasicAuthentication("DatabaseMigrationsAuth")]
    public class DataMigrationController(IMigrationRunner migrationRunner) : ControllerBase
    {
        [HttpGet("migrate")]
        public ActionResult Migrate()
        {
            migrationRunner.MigrateUp();
            return this.Ok(new { status = "OK" });
        }
    }
}
