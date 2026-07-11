using System.Text.Json;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Per-event on/off switches parsed from <see cref="HubEntity.DiscordNotificationSettings"/>
    /// (a JSON string, e.g. { "registrationOpened": true, "matchApproved": false }). Missing keys
    /// and a missing/malformed blob default to ON — a hub that configured a webhook wants events,
    /// and a broken settings string must never block (or crash) the notification flow.
    /// </summary>
    public class DiscordNotificationSettings
    {
        public bool RegistrationOpened { get; set; } = true;
        public bool RegistrationClosed { get; set; } = true;
        public bool TournamentStarted { get; set; } = true;
        public bool MatchApproved { get; set; } = true;
        public bool MatchReverted { get; set; } = true;
        public bool TournamentFinished { get; set; } = true;

        private static readonly JsonSerializerOptions ParseOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static DiscordNotificationSettings Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new DiscordNotificationSettings();

            try
            {
                return JsonSerializer.Deserialize<DiscordNotificationSettings>(json, ParseOptions)
                    ?? new DiscordNotificationSettings();
            }
            catch (JsonException)
            {
                return new DiscordNotificationSettings();
            }
        }
    }
}
