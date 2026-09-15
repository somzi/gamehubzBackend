using GameHubz.Logic.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GameHubz.Logic.Services
{
    public class NotificationService : INotificationService
    {
        private const string ExpoPushUrl = "https://exp.host/--/api/v2/push/send";
        private const int MaxTokensPerRequest = 100;

        // The same shape the Expo request is serialised with, so the inbox's stored copy of a payload
        // and the one a device receives are the same JSON.
        private static readonly JsonSerializerOptions PayloadJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private static readonly IReadOnlyDictionary<Guid, Guid> NoNotificationIds = new Dictionary<Guid, Guid>();

        private readonly IHttpClientFactory httpClientFactory;
        private readonly ILogger<NotificationService> logger;
        private readonly IServiceScopeFactory serviceScopeFactory;
        private readonly ILocalizationService localizationService;

        public NotificationService(
            IHttpClientFactory httpClientFactory,
            ILogger<NotificationService> logger,
            IServiceScopeFactory serviceScopeFactory,
            ILocalizationService localizationService)
        {
            this.httpClientFactory = httpClientFactory;
            this.logger = logger;
            this.serviceScopeFactory = serviceScopeFactory;
            this.localizationService = localizationService;
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

        public Task SendLocalizedToOneAsync(
            PushRecipient recipient,
            PushText title,
            PushText body,
            object? data = null)
            => this.SendLocalizedToManyAsync(new[] { recipient }, title, body, data);

        public async Task SendLocalizedToManyAsync(
            IEnumerable<PushRecipient> recipients,
            PushText title,
            PushText body,
            object? data = null)
        {
            // One device may be registered once per language bucket at most: de-duplicate on the
            // token so a user who somehow appears twice does not get two copies.
            var byToken = new Dictionary<string, PushRecipient>(StringComparer.Ordinal);

            // The inbox is per account, not per device: one row per user, token or no token.
            var inboxLanguages = new Dictionary<Guid, string?>();

            foreach (PushRecipient recipient in recipients)
            {
                if (recipient.UserId is Guid userId && userId != Guid.Empty)
                {
                    inboxLanguages[userId] = recipient.Language;
                }

                if (string.IsNullOrWhiteSpace(recipient.PushToken))
                {
                    continue;
                }

                byToken[recipient.PushToken] = recipient;
            }

            if (byToken.Count == 0 && inboxLanguages.Count == 0)
            {
                return;
            }

            JsonObject? payload = data == null
                ? null
                : JsonSerializer.SerializeToNode(data, PayloadJsonOptions) as JsonObject;

            // Recorded before the push goes out: every device's payload carries the id of its own inbox
            // row (tapping the push marks that row read), and the row already exists when the push lands.
            // A failure here is logged and never stops the push.
            IReadOnlyDictionary<Guid, Guid> notificationIds = inboxLanguages.Count > 0
                ? await this.RecordInInboxAsync(inboxLanguages, title, body, payload)
                : NoNotificationIds;

            // Resolve the wording once per language rather than once per device.
            foreach (var group in byToken.Values.GroupBy(recipient => recipient.Language, StringComparer.OrdinalIgnoreCase))
            {
                string? language = group.Key;
                string resolvedTitle = title.Resolve(this.localizationService, language);
                string resolvedBody = body.Resolve(this.localizationService, language);

                var messages = group
                    .Select(recipient => new ExpoPushMessage
                    {
                        To = recipient.PushToken,
                        Title = resolvedTitle,
                        Body = resolvedBody,
                        Data = PayloadFor(recipient, data, payload, notificationIds),
                    })
                    .ToList();

                foreach (var chunk in messages.Chunk(MaxTokensPerRequest))
                {
                    await SendBatchAsync(chunk.ToList());
                }
            }

            // The inbox counters go out only after the pushes: they are best-effort, one SignalR send per
            // user, and for a hub-wide announcement that loop would otherwise hold every push back.
            if (notificationIds.Count > 0)
            {
                await this.PushInboxSummariesAsync(notificationIds.Keys.ToList());
            }
        }

        private async Task PushInboxSummariesAsync(IReadOnlyCollection<Guid> userIds)
        {
            try
            {
                // Own scope, like RecordInInboxAsync: this runs after the triggering request is gone.
                using var scope = this.serviceScopeFactory.CreateScope();
                var inbox = scope.ServiceProvider.GetRequiredService<NotificationInboxService>();
                await inbox.PushSummariesAsync(userIds);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to push inbox counters to {Count} user(s).", userIds.Count);
            }
        }

        private async Task<IReadOnlyDictionary<Guid, Guid>> RecordInInboxAsync(
            Dictionary<Guid, string?> languageByUser,
            PushText title,
            PushText body,
            JsonObject? payload)
        {
            try
            {
                // Own scope and DbContext, for the same reason as the stale-token cleanup (F72): sends run
                // fire-and-forget, long after the request that triggered them disposed its own context.
                using var scope = this.serviceScopeFactory.CreateScope();
                var inbox = scope.ServiceProvider.GetRequiredService<NotificationInboxService>();
                return await inbox.RecordAsync(languageByUser, title, body, payload);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to record a notification in {Count} inbox(es).", languageByUser.Count);
                return NoNotificationIds;
            }
        }

        // A device whose user got an inbox row gets that row's id added to its payload; every other
        // device gets the payload exactly as the caller built it.
        private static object? PayloadFor(
            PushRecipient recipient,
            object? data,
            JsonObject? payload,
            IReadOnlyDictionary<Guid, Guid> notificationIds)
        {
            if (payload == null
                || recipient.UserId is not Guid userId
                || !notificationIds.TryGetValue(userId, out Guid notificationId))
            {
                return data;
            }

            var personal = (JsonObject)payload.DeepClone();
            personal["notificationId"] = notificationId.ToString();
            return personal;
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