using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class AddHubMemberRequest
    {
        public Guid UserId { get; set; }

        public HubRole Role { get; set; }
    }
}
