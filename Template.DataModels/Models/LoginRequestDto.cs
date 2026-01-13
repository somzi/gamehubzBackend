using System.ComponentModel.DataAnnotations;

namespace Template.DataModels.Models
{
    public class LoginRequestDto
    {
        [Required]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";
    }
}