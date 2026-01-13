namespace GameHubz.DataModels.Models
{
    public class UserHubDto
    {
        public Guid? Id { get; set; }

        public Guid? UserId { get; set; }

        public Guid? HubId { get; set; }

        public HubDto? Hub { get; set; }
    }
}