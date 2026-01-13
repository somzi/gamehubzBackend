namespace Template.DataModels.Models
{
    public class UserPasswordEdit
    {
        public Guid Id { get; set; }

        public string NewPassword { get; set; } = "";
    }
}