using System.ComponentModel.DataAnnotations;

namespace Template.DataModels.Tokens
{
    public class TokenRequest
    {
        public TokenRequest()
        {
        }

        [Required]
        public string AccessToken { get; set; } = "";

        [Required]
        public string RefreshToken { get; set; } = "";
    }
}