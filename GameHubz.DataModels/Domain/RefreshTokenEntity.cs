using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class RefreshTokenEntity : BaseEntity
    {
        public RefreshTokenEntity()
        {
            this.User = new UserEntity();
        }

        public string? Token { get; set; }

        public DateTime Expires { get; set; }

        public Guid UserId { get; set; }

        public UserEntity User { get; set; }
    }
}
