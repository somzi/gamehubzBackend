namespace GameHubz.DataModels.Models
{
    public class ResetPasswordRequestDto
    {
        public Guid ForgotPasswordToken { get; set; }

        public string Password { get; set; } = string.Empty;

        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
