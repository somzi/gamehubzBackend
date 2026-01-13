namespace GameHubz.DataModels.Models
{
    public class UserHubEdit
    {
        public Guid? Id { get; set; }

        public Guid? UserId { get; set; }

        public Guid? HubId { get; set; }

        public HubEdit? Hub { get; set; }
    }
}