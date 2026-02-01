namespace GameHubz.DataModels.Models
{
    public class MatchChatDto
    {
        public Guid? Id { get; set; }
        public string Content { get; set; } = "";

        public Guid? MatchId { get; set; }

        public Guid? UserId { get; set; }

        public UserDto? User { get; set; }


    }
}