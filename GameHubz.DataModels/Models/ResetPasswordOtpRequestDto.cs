namespace GameHubz.DataModels.Models
{
    public class ResetPasswordOtpRequestDto
    {
        public string OtpCode { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}