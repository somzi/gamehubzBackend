namespace GameHubz.DataModels.Models
{
    public class MatchChatEdit
    {
        public Guid? Id { get; set; }

        public string Content { get; set; } = "";

        public Guid? MatchId { get; set; }

        public Guid? UserId { get; set; }

        public UserEdit? User { get; set; }


    }
}