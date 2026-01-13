using System.Globalization;
using GameHubz.Common.Consts;
using GameHubz.Common.Enums;

namespace GameHubz.Common.Models
{
    public class TokenUserInfo
    {
        public string Username { get; set; } = "";

        /// <summary>
        /// Values from UserRoleSystemNames class
        /// </summary>
        public string Role { get; set; } = "";

        public Guid UserId { get; set; }

        public UserRoleEnum? RoleEnum { get; set; }
    }
}
