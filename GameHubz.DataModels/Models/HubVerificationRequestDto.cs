using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class HubVerificationRequestDto
    {
        public Guid Id { get; set; }

        public Guid HubId { get; set; }

        public string Reason { get; set; } = string.Empty;

        public HubVerificationStatus Status { get; set; }

        public DateTime? CreatedOn { get; set; }
    }
}
