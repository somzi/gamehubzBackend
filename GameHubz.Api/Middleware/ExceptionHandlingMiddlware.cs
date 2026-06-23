using System.Net;
using System.Text.Json;
using FluentValidation;
using GameHubz.Api.Models;
using GameHubz.Common.Consts;
using GameHubz.Data.Context;
using GameHubz.Data.Exceptions;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Exceptions.Base;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GameHubz.Api.Middleware
{
    public class ExceptionHandlingMiddlware
    {
        private const int MaxRequestBodyLength = 8000;

        private static readonly NLog.ILogger FallbackLogger = NLog.LogManager.GetCurrentClassLogger();

        private readonly RequestDelegate next;
        private readonly LoggerService logger;
        private readonly IServiceScopeFactory scopeFactory;
        private readonly ILocalizationService localizationService;
        private readonly IHostEnvironment environment;

        public ExceptionHandlingMiddlware(
            RequestDelegate next,
            LoggerService logger,
            IServiceScopeFactory scopeFactory,
            ILocalizationService localizationService,
            IHostEnvironment environment)
        {
            this.next = next;
            this.logger = logger;
            this.scopeFactory = scopeFactory;
            this.localizationService = localizationService;
            this.environment = environment;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                context.Request.EnableBuffering();
                await this.next(context);
            }
            catch (CoreException ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.InternalServerError, ex, ExceptionCategory.Core);
            }
            catch (BaseUnauthorizedException ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.Unauthorized, ex, ExceptionCategory.Unauthorized);
            }
            catch (BaseDataException ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.BadRequest, ex, ExceptionCategory.Data);
            }
            catch (BaseException ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.BadRequest, ex, ExceptionCategory.Handled);
            }
            catch (ValidationException ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.BadRequest, ex, ExceptionCategory.Validation);
            }
            catch (System.UnauthorizedAccessException ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.Unauthorized, ex, ExceptionCategory.Unauthorized);
            }
            catch (Exception ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.InternalServerError, ex, ExceptionCategory.Unhandled);
            }
        }

        private async Task CreateExceptionResponse(HttpContext context, HttpStatusCode code, Exception exception, string category)
        {
            var errorId = Guid.NewGuid();
            int statusCode = (int)code;

            // Read the buffered request body once, before LoggerService consumes/disposes
            // the stream, so it can be stored alongside the error.
            string requestBody = await ReadRequestBodySafe(context.Request);

            // Existing structured file logging (NLog) — kept as the always-on log sink.
            await this.logger.LogError(context, exception, category);

            // Persist server faults (5xx) to the ErrorLog table for triage. 4xx are expected
            // user/validation errors and would only add noise. Best-effort: a logging failure
            // must never replace the original error response.
            bool persisted = false;
            if (statusCode >= 500)
            {
                persisted = await this.TryPersistErrorLog(context, exception, category, statusCode, errorId, requestBody);
            }

            string result = this.CreateHandledErrorModel(exception, statusCode, persisted ? errorId : (Guid?)null);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(result);
        }

        private string CreateHandledErrorModel(Exception exception, int statusCode, Guid? errorId)
        {
            bool isServerError = statusCode >= 500;

            var obj = new HandledErrorModel()
            {
                // Server faults never reveal raw internal text — the user gets a safe, generic
                // message. 4xx messages are localized and user-actionable, so they pass through.
                Message = isServerError
                    ? this.localizationService["Common.UnexpectedError"]
                    : exception.Message,

                // Stack traces only ever reach the client in Development; production stays clean.
                Details = this.environment.IsDevelopment() ? exception.ToString() : string.Empty,

                ErrorId = errorId?.ToString(),
            };

            if (exception is ValidationException validationException)
            {
                obj.Items = validationException.Errors.Select(x => new ValidationErrorItem()
                {
                    Property = $"{char.ToLower(x.PropertyName[0])}{x.PropertyName[1..]}",
                    Message = x.ErrorMessage
                }).ToList();
            }

            return JsonSerializer.Serialize(obj);
        }

        private async Task<bool> TryPersistErrorLog(
            HttpContext context,
            Exception exception,
            string category,
            int statusCode,
            Guid errorId,
            string requestBody)
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                HttpRequest request = context.Request;

                var entity = new ErrorLogEntity
                {
                    Id = errorId,
                    CreatedOn = now,
                    ModifiedOn = now,
                    IsDeleted = false,
                    UserId = TryGetUserId(context),
                    Category = category,
                    ExceptionType = exception.GetType().Name,
                    Message = Truncate(exception.Message, 4000) ?? string.Empty,
                    StackTrace = exception.ToString(),
                    Source = Truncate(exception.Source, 512),
                    StatusCode = statusCode,
                    RequestMethod = Truncate(request.Method, 16) ?? string.Empty,
                    RequestPath = Truncate(request.Path.ToString(), 1024) ?? string.Empty,
                    QueryString = Truncate(request.QueryString.ToString(), 2048),
                    RequestBody = string.IsNullOrEmpty(requestBody) ? null : requestBody,
                    UserAgent = Truncate(request.Headers.UserAgent.ToString(), 1024),
                    AppVersion = Truncate(GetHeader(request, "X-App-Version"), 64),
                    // Prefer the explicit X-Platform header; fall back to sniffing the
                    // User-Agent so old app builds still record ios/android instead of null.
                    Platform = Truncate(
                        GetHeader(request, "X-Platform") ?? InferPlatformFromUserAgent(request.Headers.UserAgent.ToString()),
                        64),
                    IpAddress = Truncate(ResolveClientIp(context), 64),
                    IsResolved = false,
                };

                // Fresh scope + DbContext so error logging is isolated from the request-scoped
                // context that may have just faulted.
                using IServiceScope scope = this.scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
                db.Set<ErrorLogEntity>().Add(entity);
                await db.SaveChangesAsync();

                return true;
            }
            catch (Exception persistEx)
            {
                // Never let error-logging failure break the response — fall back to NLog only.
                FallbackLogger.Error(persistEx, "Failed to persist ErrorLog {0}", errorId);
                return false;
            }
        }

        private static async Task<string> ReadRequestBodySafe(HttpRequest request)
        {
            try
            {
                if (request.Body == null || !request.Body.CanSeek)
                {
                    return string.Empty;
                }

                // Multipart/binary uploads aren't useful as text and can be huge — skip them.
                string contentType = request.ContentType ?? string.Empty;
                if (contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                request.Body.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(request.Body, leaveOpen: true);
                string body = await reader.ReadToEndAsync();
                request.Body.Seek(0, SeekOrigin.Begin);

                return body.Length > MaxRequestBodyLength ? body[..MaxRequestBodyLength] : body;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static Guid? TryGetUserId(HttpContext context)
        {
            string? rawId = context.User.Identities
                .SelectMany(identity => identity.Claims)
                .FirstOrDefault(claim => claim.Type.Equals("id", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            return Guid.TryParse(rawId, out Guid userId) ? userId : null;
        }

        private static string? GetHeader(HttpRequest request, string name)
            => request.Headers.TryGetValue(name, out var value) && value.Count > 0
                ? value.ToString()
                : null;

        /// <summary>
        /// Returns the original client IP. The API runs behind a reverse proxy in Docker,
        /// so <c>Connection.RemoteIpAddress</c> is always the proxy container IP — the real
        /// client address lives in the leftmost entry of <c>X-Forwarded-For</c>.
        /// </summary>
        private static string? ResolveClientIp(HttpContext context)
        {
            string forwarded = context.Request.Headers["X-Forwarded-For"].ToString();

            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                return forwarded.Split(',')[0].Trim();
            }

            return context.Connection.RemoteIpAddress?.ToString();
        }

        /// <summary>
        /// Best-effort platform sniff for old app builds that don't yet send X-Platform.
        /// React Native on Android goes through OkHttp; iOS uses Apple's CFNetwork/Darwin.
        /// </summary>
        private static string? InferPlatformFromUserAgent(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent))
            {
                return null;
            }

            if (userAgent.Contains("okhttp", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
            {
                return "android";
            }

            if (userAgent.Contains("CFNetwork", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("Darwin", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("iOS", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
            {
                return "ios";
            }

            return null;
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
