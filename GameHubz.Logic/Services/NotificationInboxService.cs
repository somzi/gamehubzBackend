using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// The notification inbox: a per-user record of the notifications GameHubz sends, so a push that was
    /// swiped away, cleared by the OS, or never delivered at all (no push token) is not lost.
    /// <see cref="NotificationService"/> writes the rows at send time; the signed-in user reads and clears
    /// them through the endpoints below, and every change to the counters is pushed live to the user's
    /// devices through <see cref="UserHub"/> ("NotificationsUpdated").
    ///
    /// Two separate markers, on purpose. <c>SeenOn</c> is set for everything at once when the inbox is
    /// opened and is what clears the bell — a badge that only an item-by-item read could clear would sit
    /// on a busy organizer's home screen forever and stop meaning anything. <c>ReadOn</c> is per row
    /// (opened, or tapped as a push) and is what the unread styling and the tab counts show.
    /// </summary>
    public class NotificationInboxService : AppBaseService
    {
        public const string SummaryUpdatedEvent = "NotificationsUpdated";

        private const int DefaultPageSize = 30;
        private const int MaxPageSize = 50;

        // Payload types that ask the recipient to do something. Everything else — an untyped push
        // included — is an update. Chat messages never reach the inbox (they have their own unread
        // surfaces), so they have no entry here.
        private static readonly HashSet<string> ActionTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "checkIn",
            "roundDeadline",
            "opponentReady",
            "resultProposed",
            "teamTieBreak",
            "adminHelp",
            "hubJoinRequest",
            "teamJoinRequest",
            "friend_request",
            "scheduleCleared",
            "matchAvailability",
        };

        private readonly IHubContext<UserHub> hubContext;

        public NotificationInboxService(
            IUnitOfWorkFactory factory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            IHubContext<UserHub> hubContext)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.hubContext = hubContext;
        }

        public async Task<NotificationPageDto> GetPage(string? category, int? take, string? before)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            int pageSize = Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize);

            // One row past the page says whether anything older exists, without a count query.
            var rows = await this.AppUnitOfWork.NotificationRepository.GetPage(
                user.UserId, ParseCategory(category), ParseCursor(before), pageSize + 1);

            bool hasMore = rows.Count > pageSize;
            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            return new NotificationPageDto
            {
                Items = rows.Select(ToDto).ToList(),
                // Raw ticks rather than the serialised timestamp: the JSON converter writes milliseconds,
                // the column keeps microseconds, and a truncated cursor would skip rows at the page edge.
                NextCursor = hasMore
                    ? rows[^1].CreatedOn!.Value.Ticks.ToString(CultureInfo.InvariantCulture)
                    : null,
            };
        }

        public async Task<NotificationSummaryDto> GetSummary()
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            return await this.ComputeSummaryAsync(user.UserId);
        }

        /// <summary>The user opened the inbox: clears the bell. Rows keep their unread styling until opened.</summary>
        public async Task<NotificationSummaryDto> MarkSeen()
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            await this.AppUnitOfWork.NotificationRepository.MarkSeen(user.UserId, DateTime.UtcNow);
            return await this.SyncUserAsync(user.UserId);
        }

        public async Task<NotificationSummaryDto> MarkRead(Guid notificationId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            await this.AppUnitOfWork.NotificationRepository.MarkRead(user.UserId, notificationId, DateTime.UtcNow);
            return await this.SyncUserAsync(user.UserId);
        }

        public async Task<NotificationSummaryDto> MarkAllRead(string? category)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            await this.AppUnitOfWork.NotificationRepository.MarkAllRead(user.UserId, ParseCategory(category), DateTime.UtcNow);
            return await this.SyncUserAsync(user.UserId);
        }

        /// <summary>
        /// Writes one inbox row per user for a notification that is being sent, worded in each user's own
        /// language exactly as their push is, and returns each user's new row id so the push can carry it.
        /// The text is stored resolved rather than as a key: a user who later switches language still
        /// reads the message they were actually sent.
        /// <para>
        /// Called by <see cref="NotificationService"/> on a scope of its own. Touches no request user, so
        /// it is safe from a background send.
        /// </para>
        /// </summary>
        public async Task<IReadOnlyDictionary<Guid, Guid>> RecordAsync(
            IReadOnlyDictionary<Guid, string?> languageByUser,
            PushText title,
            PushText body,
            JsonObject? payload)
        {
            var ids = new Dictionary<Guid, Guid>(languageByUser.Count);
            if (languageByUser.Count == 0)
            {
                return ids;
            }

            string? type = ReadType(payload);
            NotificationCategory category = type != null && ActionTypes.Contains(type)
                ? NotificationCategory.Action
                : NotificationCategory.Update;
            string? dataJson = payload?.ToJsonString();

            // Resolved once per language rather than once per user.
            var wording = new Dictionary<string, (string Title, string Body)>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<NotificationEntity>(languageByUser.Count);

            foreach (var (userId, language) in languageByUser)
            {
                string key = language ?? string.Empty;
                if (!wording.TryGetValue(key, out var text))
                {
                    text = (title.Resolve(this.LocalizationService, language), body.Resolve(this.LocalizationService, language));
                    wording[key] = text;
                }

                Guid id = Guid.NewGuid();
                ids[userId] = id;

                rows.Add(new NotificationEntity
                {
                    Id = id,
                    UserId = userId,
                    Type = type,
                    Category = category,
                    Title = text.Title,
                    Body = text.Body,
                    DataJson = dataJson,
                });
            }

            this.AppUnitOfWork.NotificationRepository.AddRange(rows);
            await this.SaveAsync();

            // No counter push here: NotificationService sends it once the pushes are out, so a large
            // announcement is not held back by one SignalR send per recipient.
            return ids;
        }

        /// <summary>
        /// Pushes fresh counters to each user's devices — one grouped query however many users. Best-effort,
        /// like every badge push: the app also refetches on foreground and whenever the hub reconnects.
        /// </summary>
        public async Task PushSummariesAsync(IReadOnlyCollection<Guid> userIds)
        {
            try
            {
                var summaries = await this.AppUnitOfWork.NotificationRepository.GetSummaries(userIds);

                foreach (Guid userId in userIds)
                {
                    await this.hubContext.Clients
                        .Group(UserHub.GroupName(userId))
                        .SendAsync(SummaryUpdatedEvent, summaries.TryGetValue(userId, out var summary) ? summary : new NotificationSummaryDto());
                }
            }
            catch
            {
                // best-effort — never let a counter push break the send that triggered it
            }
        }

        // A read or seen change on one device is pushed to the user's group too, so their other devices
        // clear the same badge. The acting device gets the same numbers in the HTTP response.
        private async Task<NotificationSummaryDto> SyncUserAsync(Guid userId)
        {
            var summary = await this.ComputeSummaryAsync(userId);

            try
            {
                await this.hubContext.Clients
                    .Group(UserHub.GroupName(userId))
                    .SendAsync(SummaryUpdatedEvent, summary);
            }
            catch
            {
                // best-effort — the caller has the summary in the response regardless
            }

            return summary;
        }

        private async Task<NotificationSummaryDto> ComputeSummaryAsync(Guid userId)
        {
            var summaries = await this.AppUnitOfWork.NotificationRepository.GetSummaries(new[] { userId });
            return summaries.TryGetValue(userId, out var summary) ? summary : new NotificationSummaryDto();
        }

        private static NotificationDto ToDto(NotificationEntity row) => new()
        {
            Id = row.Id!.Value,
            Type = row.Type,
            Category = row.Category,
            Title = row.Title,
            Body = row.Body,
            Data = ParseData(row.DataJson),
            CreatedOn = row.CreatedOn!.Value,
            ReadOn = row.ReadOn,
        };

        private static JsonElement? ParseData(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // A row the app cannot route is still worth showing; it just opens nothing.
                return null;
            }
        }

        private static string? ReadType(JsonObject? payload)
        {
            if (payload != null
                && payload.TryGetPropertyValue("type", out JsonNode? node)
                && node is JsonValue value
                && value.TryGetValue(out string? type)
                && !string.IsNullOrWhiteSpace(type))
            {
                return type;
            }

            return null;
        }

        private static NotificationCategory? ParseCategory(string? category)
            => category?.Trim().ToLowerInvariant() switch
            {
                "action" => NotificationCategory.Action,
                "update" => NotificationCategory.Update,
                _ => null,
            };

        private static DateTime? ParseCursor(string? cursor)
            => long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
                && ticks > 0
                && ticks <= DateTime.MaxValue.Ticks
                ? new DateTime(ticks, DateTimeKind.Utc)
                : null;
    }
}
