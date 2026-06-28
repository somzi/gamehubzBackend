using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using GameHubz.Common.Consts;
using GameHubz.Logic.Services;
using GameHubz.Logic.Utility;

namespace GameHubz.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : ControllerBase
    {
        private readonly IConfiguration configuration;
        private readonly BlobService blobService;
        private readonly EmailService emailService;
        private readonly DateTimeProvider dateTimeProvider;
        private readonly ILogger<TestController> logger;
        private readonly AppAuthorizationService appAuthorizationService;

        public TestController(
            IConfiguration configuration,
            BlobService blobService,
            EmailService emailService,
            DateTimeProvider dateTimeProvider,
            ILogger<TestController> logger,
            AppAuthorizationService appAuthorizationService)
        {
            this.configuration = configuration;
            this.blobService = blobService;
            this.emailService = emailService;
            this.dateTimeProvider = dateTimeProvider;
            this.logger = logger;
            this.appAuthorizationService = appAuthorizationService;
        }

        // F108: this diagnostic runs a DB probe, an Azure-storage write, AND sends an email, then dumps
        // raw exception details (stack traces, connection info) into the HTML response. It was reachable
        // anonymously — anyone could trigger mail sends and harvest internal state. Restrict to Admin.
        // (health/version below stay anonymous so uptime monitors keep working.)
        [Authorize]
        [HttpGet("")]
        public async Task<ContentResult> Index()
        {
            await this.appAuthorizationService.CheckAuthorization([UserRoleEnum.Admin]);

            List<TestResult> results = new()
            {
                await RunTest("Database connection test", this.TestDatabaseConnection),
                await RunTest("Logger test (check in database)", this.TestLogging),
                await RunTest("Azure storage test", this.TestAzureStorage),
                await RunTest("Email test", this.TestSendEmail)
            };

            string output = string.Join("<br />", results.Select(x => x.GetHtml()));
            output += $"<br />{dateTimeProvider.Now()}";

            return new ContentResult
            {
                ContentType = "text/html",
                Content = output
            };
        }

        [HttpGet("health")]
        public async Task<ContentResult> Health()
        {
            string output = "Server is ON!!!";

            return new ContentResult
            {
                ContentType = "text/html",
                Content = output
            };
        }

        [HttpGet("version")]
        public ContentResult Version()
        {
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown";

            return new ContentResult
            {
                ContentType = "text/html",
                Content = version
            };
        }

        private Task TestLogging()
        {
            logger.LogError("Test error log");
            return Task.CompletedTask;
        }

        private Task TestSendEmail()
        {
            return this.emailService.SendEmail(new DataModels.Models.EmailModel()
            {
                To = this.configuration.GetStringThrowIfNull("TestEmailReceiver"),
                IsMessageHtml = true,
                Message = "Test",
                Subject = "Test",
            });
        }

        private async Task TestAzureStorage()
        {
            Stream stream = new MemoryStream(new byte[] { 1 });
            string blobName = $"{DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss")}.bin";
            string container = "test";
            await blobService.Upload(stream, container, blobName);
            await blobService.Delete(blobName, container);
        }

        private async Task TestDatabaseConnection()
        {
            string connectionString = this.configuration["ConnectionStrings:DatabaseConnection"]!;
            SqlConnection connection = new(connectionString);
            connection.Open();
            connection.Close();

            await Task.CompletedTask;
        }

        private static async Task<TestResult> RunTest(string testName, Func<Task> testAction)
        {
            try
            {
                await testAction();
                return new TestResult(testName, true);
            }
            catch (Exception ex)
            {
                return new TestResult(testName, false, ex.ToString());
            }
        }
    }

    public class TestResult
    {
        public TestResult(string testName, bool isSuccessful, string message = "")
        {
            this.IsSuccessful = isSuccessful;
            this.TestName = testName;
            this.Message = message;
        }

        public bool IsSuccessful { get; }
        public string TestName { get; }
        public string Message { get; }

        public string GetHtml()
        {
            string status = this.IsSuccessful ? "<span style='color:green'>OK</span>" : "<span style='color:red'>FAILED</span>";
            string message = $"<div>{this.Message.Replace(Environment.NewLine, "<br />")}</div>";
            return $"<b>{this.TestName}</b>: {status} {message}";
        }
    }
}