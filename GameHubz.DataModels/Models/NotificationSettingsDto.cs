using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using GameHubz.DataModels.Domain;

namespace GameHubz.DataModels.Models
{
    public class NotificationSettingsDto
    {
        public bool ModeratedChatNotifications { get; set; } = true;

        // Null on PUT means omitted by an older app: preserve that list. GET always returns arrays.
        [MaxLength(2000)]
        public List<Guid>? MutedHubIds { get; set; }

        [MaxLength(2000)]
        public List<Guid>? MutedTournamentIds { get; set; }

        public static List<Guid> ReadIds(string? json)
            => string.IsNullOrEmpty(json) ? new() : JsonSerializer.Deserialize<List<Guid>>(json) ?? new();

        public static NotificationSettingsDto FromUser(UserEntity user) => new()
        {
            ModeratedChatNotifications = user.ModeratedChatNotifications,
            MutedHubIds = ReadIds(user.MutedHubIdsJson),
            MutedTournamentIds = ReadIds(user.MutedTournamentIdsJson),
        };

        public void ApplyTo(UserEntity user)
        {
            user.ModeratedChatNotifications = ModeratedChatNotifications;
            if (MutedHubIds != null)
                user.MutedHubIdsJson = JsonSerializer.Serialize(MutedHubIds.Where(id => id != Guid.Empty).Distinct());
            if (MutedTournamentIds != null)
                user.MutedTournamentIdsJson = JsonSerializer.Serialize(MutedTournamentIds.Where(id => id != Guid.Empty).Distinct());
        }
    }
}
