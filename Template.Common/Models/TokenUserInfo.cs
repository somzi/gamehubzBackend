using System.Globalization;
using Template.Common.Consts;
using Template.Common.Enums;

namespace Template.Common.Models
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