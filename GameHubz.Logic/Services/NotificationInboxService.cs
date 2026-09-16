using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameHubz.DataModels.Consts;
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
        private const int SummaryPushBatchSize = 50;

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
            "lineupMissing",
        };

        private readonly IHubContext<UserHub> hubContext;
        private readonly int retentionDays;

        public NotificationInboxService(
            IUnitOfWorkFactory factory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            IHubContext<UserHub> hubContext,
            // Optional so a test can build the service without configuration; DI always supplies it.
            Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.hubContext = hubContext;
            this.retentionDays = NotificationRetentionRules.ResolveRetentionDays(
                configuration?[NotificationRetentionRules.RetentionDaysConfigKey] is string raw
                    && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int days)
                    ? days
                    : null);
        }

        public async Task<NotificationPageDto> GetPage(string? category, int? take, string? before)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            int pageSize = Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize);
            NotificationCursor? cursor = ParseCursor(before);

            // A cursor this server never issued is a client bug. Answered with page one, it sent an
            // infinite scroll round and round the newest rows without anyone noticing; a 400 surfaces it.
            if (cursor == null && !string.IsNullOrWhiteSpace(before))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.InvalidNotificationCursor"]);

            // One row past the page says whether anything older exists, without a count query.
            var rows = await this.AppUnitOfWork.NotificationRepository.GetPage(
                user.UserId,
                ParseCategory(category),
                cursor?.CreatedOn,
                cursor?.Id,
                pageSize + 1);

            bool hasMore = rows.Count > pageSize;
            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            return new NotificationPageDto
            {
                Items = rows.Select(ToDto).ToList(),
                // CreatedOn is the main key and Id is its deterministic tie-breaker. Raw ticks preserve
                // sub-millisecond precision that the JSON timestamp converter does not expose.
                NextCursor = hasMore
                    ? FormatCursor(rows[^1])
                    : null,
                RetentionDays = this.retentionDays,
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
            => (await this.RecordBatchAsync(new[] { new InboxRecord(languageByUser, title, body, payload) }))[0];

        /// <summary>One notification to record: who gets a row, its wording and its payload.</summary>
        public readonly record struct InboxRecord(
            IReadOnlyDictionary<Guid, string?> LanguageByUser,
            PushText Title,
            PushText Body,
            JsonObject? Payload);

        /// <summary>
        /// <see cref="RecordAsync"/> for several notifications in one save — a sweep that reminds the players
        /// of a whole round writes its rows together instead of one insert per match. Returns each
        /// notification's user → row id map, in the order they were given.
        /// </summary>
        public async Task<IReadOnlyList<IReadOnlyDictionary<Guid, Guid>>> RecordBatchAsync(IReadOnlyList<InboxRecord> records)
        {
            var result = new List<IReadOnlyDictionary<Guid, Guid>>(records.Count);
            var rows = new List<NotificationEntity>();

            foreach (InboxRecord record in records)
            {
                var ids = new Dictionary<Guid, Guid>(record.LanguageByUser.Count);
                result.Add(ids);

                if (record.LanguageByUser.Count == 0)
                {
                    continue;
                }

                string? type = ReadType(record.Payload);
                NotificationCategory category = type != null && ActionTypes.Contains(type)
                    ? NotificationCategory.Action
                    : NotificationCategory.Update;
                string? dataJson = record.Payload?.ToJsonString();

                // Resolved once per language rather than once per user.
                var wording = new Dictionary<string, (string Title, string Body)>(StringComparer.OrdinalIgnoreCase);

                foreach (var (userId, language) in record.LanguageByUser)
                {
                    string key = language ?? string.Empty;
                    if (!wording.TryGetValue(key, out var text))
                    {
                        text = (record.Title.Resolve(this.LocalizationService, language), record.Body.Resolve(this.LocalizationService, language));
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
            }

            if (rows.Count > 0)
            {
                this.AppUnitOfWork.NotificationRepository.AddRange(rows);
                await this.SaveAsync();
            }

            // No counter push here: NotificationService sends it once the pushes are out, so a large
            // announcement is not held back by one SignalR send per recipient.
            return result;
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

                // SignalR sends are independent, but launching an unbounded Task.WhenAll for a very
                // large retention sweep would simply move the bottleneck into allocations/socket
                // pressure. Bounded batches remove the N-users serial latency while keeping load
                // predictable. One disconnected user's send remains best-effort and does not stop
                // later users from receiving their corrected counters.
                foreach (Guid[] batch in userIds.Distinct().Chunk(SummaryPushBatchSize))
                {
                    await Task.WhenAll(batch.Select(async userId =>
                    {
                        try
                        {
                            await this.hubContext.Clients
                                .Group(UserHub.GroupName(userId))
                                .SendAsync(
                                    SummaryUpdatedEvent,
                                    summaries.TryGetValue(userId, out var summary)
                                        ? summary
                                        : new NotificationSummaryDto());
                        }
                        catch
                        {
                            // Best-effort per user. The app refetches on foreground/reconnect.
                        }
                    }));
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

        private readonly record struct NotificationCursor(DateTime CreatedOn, Guid? Id);

        private static string FormatCursor(NotificationEntity row)
            => string.Concat(
                row.CreatedOn!.Value.Ticks.ToString(CultureInfo.InvariantCulture),
                ":",
                row.Id!.Value.ToString("N"));

        private static NotificationCursor? ParseCursor(string? cursor)
        {
            if (string.IsNullOrWhiteSpace(cursor))
            {
                return null;
            }

            int separator = cursor.IndexOf(':');
            string ticksText = separator >= 0 ? cursor[..separator] : cursor;

            if (!long.TryParse(ticksText, NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
                || ticks <= 0
                || ticks > DateTime.MaxValue.Ticks)
            {
                return null;
            }

            // Accept the old ticks-only cursor so an already-open app can continue paging across a
            // backend rollout. Every cursor returned from this version includes the row id.
            if (separator < 0)
            {
                return new NotificationCursor(new DateTime(ticks, DateTimeKind.Utc), null);
            }

            string idText = cursor[(separator + 1)..];
            return Guid.TryParseExact(idText, "N", out Guid id)
                ? new NotificationCursor(new DateTime(ticks, DateTimeKind.Utc), id)
                : null;
        }
    }
}
