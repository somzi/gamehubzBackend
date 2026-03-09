namespace GameHubz.DataMigrations
{
    [Migration(23, "Added user otp")]
    public class Migration_00023_Add_User_Otp : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("User").AddColumn("ForgotPasswordOtp").AsString().Nullable();
        }
    }
}