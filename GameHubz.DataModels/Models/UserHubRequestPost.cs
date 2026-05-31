using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class UserHubRequestPost : IEditableDto
    {
        public Guid? Id { get; set; }
        public Guid? HubId { get; set; }
        public Guid? UserId { get; set; }
    }
}
