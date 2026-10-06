using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TournamentPhoneDecisionRequest
    {
        // A pending row may have been replaced since the organizer opened their inbox.
        public Guid UserDeviceId { get; set; }
    }

    // DeviceId is the installation id the phone keeps; a phone that has never verified receives it here.
    public record TournamentPhoneBindingDto(Guid DeviceId, TournamentPlayerDeviceStatus Status);

    public class TournamentPhoneDto
    {
        public Guid UserDeviceId { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string? DeviceModel { get; set; }
    }

    public class TournamentPhoneRequestDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public TournamentPhoneDto? ActivePhone { get; set; }
        public TournamentPhoneDto RequestedPhone { get; set; } = new();
        public DateTime RequestedOn { get; set; }
        public List<MatchVerificationDeviceAccountDto> OtherAccounts { get; set; } = new();
    }
}
