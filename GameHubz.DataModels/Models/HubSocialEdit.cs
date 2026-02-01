namespace GameHubz.DataModels.Models
{
    public class HubSocialEdit
    {
        public Guid? Id { get; set; }

        public string Username { get; set; } = "";

        public int Type { get; set; }

        public Guid? HubId { get; set; }

        public HubEdit? Hub { get; set; }
    }
}