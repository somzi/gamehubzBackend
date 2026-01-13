using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
	public class UserEntity : BaseEntity
	{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

		public UserEntity()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
		{
			this.RefreshTokens = new List<RefreshTokenEntity>();
		}

		public string FirstName { get; set; } = "";

		public string LastName { get; set; } = "";

        public string? ObjectId { get; set; } = "";

        public string Email { get; set; } = "";

		public string Password { get; set; } = "";

		public string PasswordNonce { get; set; } = "";

		public Guid UserRoleId { get; set; }

		public List<RefreshTokenEntity> RefreshTokens { get; }

		public UserRoleEntity UserRole { get; set; }

		public string? Language { get; set; }

		public Guid? ForgotPasswordToken { get; set; }

		public DateTime? ForgotPasswordTokenExpires { get; set; }

		public Guid? VerifyEmailToken { get; set; }

		public DateTime? VerifyEmailTokenExpires { get; set; }

		public bool IsVerified { get; set; }

		public bool IsNativeAuthentication { get; set; }
	}
}
