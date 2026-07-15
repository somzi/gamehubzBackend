using GameHubz.Common;
using GameHubz.DataModels.Enums;

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

        public string Username { get; set; } = "";

        public string? Nickname { get; set; } = "";

        public RegionType Region { get; set; }

        /// <summary>
        /// ISO 3166-1 alpha-2 country code (e.g. "RS"), or null when the user hasn't set one.
        /// Once set it locks (only null → value transitions are allowed via the API); selecting a
        /// country also derives <see cref="Region"/> from <see cref="Catalog.CountryCatalog"/>.
        /// </summary>
        public string? Country { get; set; }

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

        public List<UserHubEntity>? UserHubs { get; set; } = new();

        public List<TournamentRegistrationEntity>? TournamentRegistrations { get; set; } = new();

        public List<UserSocialEntity>? UserSocials { get; set; } = new();

        public List<TournamentParticipantEntity>? TournamentParticipants { get; set; } = new();

        public string? AvatarUrl { get; set; }

        public bool IsActive { get; set; } = true;

        public string? ForgotPasswordOtp { get; set; }

        public string? PushToken { get; set; }

        /// <summary>Discord snowflake id of the linked account (OAuth identify). Null = not linked.</summary>
        public string? DiscordUserId { get; set; }

        /// <summary>Discord username captured at link time (display only).</summary>
        public string? DiscordUsername { get; set; }

        /// <summary>Per-user switch for bot DM notifications; only meaningful while linked.</summary>
        public bool DiscordDmEnabled { get; set; } = true;

        /// <summary>Per-user switch for showing the linked Discord as a public profile link
        /// (discord.com/users deep link); only meaningful while linked. Independent of DM notifications.</summary>
        public bool DiscordShowOnProfile { get; set; } = true;
    }
}