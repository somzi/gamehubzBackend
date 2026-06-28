using GameHubz.Logic.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHubz.Logic.Services
{
    public class NotificationService : INotificationService
    {
        private const string ExpoPushUrl = "https://exp.host/--/api/v2/push/send";
        private const int MaxTokensPerRequest = 100;

        private readonly IHttpClientFactory httpClientFactory;
        private readonly ILogger<NotificationService> logger;
        private readonly IServiceScopeFactory serviceScopeFactory;

        public NotificationService(
            IHttpClientFactory httpClientFactory,
            ILogger<NotificationService> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            this.httpClientFactory = httpClientFactory;
            this.logger = logger;
            this.serviceScopeFactory = serviceScopeFactory;
        }

        public async Task SendToOneAsync(string pushToken, string title, string body, object? data = null)
        {
            if (string.IsNullOrWhiteSpace(pushToken))
                return;

            var messages = new List<ExpoPushMessage>
            {
                new ExpoPushMessage
                {
                    To = pushToken,
                    Title = title,
                    Body = body,
                    Data = data
                }
            };

            await SendBatchAsync(messages);
        }

        public async Task SendToManyAsync(IEnumerable<string> pushTokens, string title, string body, object? data = null)
        {
            var tokens = pushTokens
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();

            if (tokens.Count == 0)
                return;

            var allMessages = tokens.Select(token => new ExpoPushMessage
            {
                To = token,
                Title = title,
                Body = body,
                Data = data
            }).ToList();

            // Expo limits each request to 100 notifications – split into chunks
            foreach (var chunk in allMessages.Chunk(MaxTokensPerRequest))
            {
                await SendBatchAsync(chunk.ToList());
            }
        }

        private async Task SendBatchAsync(List<ExpoPushMessage> messages)
        {
            try
            {
                var client = httpClientFactory.CreateClient("ExpoPush");

                var response = await client.PostAsJsonAsync(ExpoPushUrl, messages, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    logger.LogWarning("Expo Push API returned {StatusCode}: {Body}", response.StatusCode, errorBody);
                    return;
                }

                var result = await response.Content.ReadFromJsonAsync<ExpoPushResponse>();

                if (result?.Data == null)
                    return;

                // F72: stale-token cleanup must run on its OWN DbContext, never the request-scoped one.
                // NotificationService is invoked fire-and-forget after the request scope is gone, and a
                // `using` over the shared UnitOfWork previously disposed the request's context mid-flight.
                // A dedicated DI scope gives us a fresh context that we own and dispose here.
                using var scope = this.serviceScopeFactory.CreateScope();
                var scopedUnitOfWorkFactory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
                using var uow = scopedUnitOfWorkFactory.CreateAppUnitOfWork();

                for (int i = 0; i < result.Data.Count; i++)
                {
                    var ticket = result.Data[i];

                    if (ticket.Status == "error" &&
                        string.Equals(ticket.Details?.Error, "DeviceNotRegistered", StringComparison.OrdinalIgnoreCase))
                    {
                        string staleToken = messages[i].To;
                        await uow.UserRepository.ClearPushTokenAsync(staleToken);
                        logger.LogWarning(
                            "DeviceNotRegistered – cleared push token for message id {MessageId}.",
                            ticket.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send push notifications via Expo");
            }
        }

        #region Expo API Models

        private sealed class ExpoPushMessage
        {
            public string To { get; set; } = "";
            public string Title { get; set; } = "";
            public string Body { get; set; } = "";
            public object? Data { get; set; }
            public string Sound { get; set; } = "default";
        }

        private sealed class ExpoPushResponse
        {
            [JsonPropertyName("data")]
            public List<ExpoPushTicket>? Data { get; set; }
        }

        private sealed class ExpoPushTicket
        {
            [JsonPropertyName("status")]
            public string? Status { get; set; }

            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("details")]
            public ExpoPushTicketDetails? Details { get; set; }
        }

        private sealed class ExpoPushTicketDetails
        {
            [JsonPropertyName("error")]
            public string? Error { get; set; }
        }

        #endregion Expo API Models
    }
}