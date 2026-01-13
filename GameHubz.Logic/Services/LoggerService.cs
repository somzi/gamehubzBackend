using Microsoft.AspNetCore.Http;
using NLog;

namespace GameHubz.Logic.Services
{
    public class LoggerService
    {
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public async void LogError(HttpContext context, Exception ex, string exceptionCategory)
        {
            var exceptionType = ex.GetType().Name;
            var requestMethod = context.Request.Method ?? string.Empty;
            var requestBody = await GetRequestBody(context.Request);
            var requestUrl = context.Request.Path.ToString() ?? string.Empty;
            var userId = GetUserId(context);

            LogEventInfo logEventInfo = new(LogLevel.Error, string.Empty, string.Empty)
            {
                Message = string.Format(ex.ToString()),
                LoggerName = logger.Name
            };

            logEventInfo.Properties.Add("userId", userId);
            logEventInfo.Properties.Add("requestUrl", requestUrl);
            logEventInfo.Properties.Add("exceptionCategory", exceptionCategory);
            logEventInfo.Properties.Add("exceptionType", exceptionType);
            logEventInfo.Properties.Add("requestMethod", requestMethod);
            logEventInfo.Properties.Add("requestBody", requestBody);
            logger.Log(logEventInfo);
        }

        private static string? GetUserId(HttpContext context)
        {
            var userIdentities = context.User.Identities.Select(x => x.Claims.Where(x => x.Type.Contains("id")))
                .FirstOrDefault();

            var userId = userIdentities?.FirstOrDefault(x => x.Type.Equals("id"))?.Value;

            return userId;
        }

        private static async Task<string> GetRequestBody(HttpRequest request)
        {
            using StreamReader stream = new(request.Body);

            stream.BaseStream.Seek(0, SeekOrigin.Begin);
            string body = await stream.ReadToEndAsync();

            return body;
        }
    }
}
