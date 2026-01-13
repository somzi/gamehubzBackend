using System.Data;
using FluentMigrator;

namespace Template.DataMigrations
{
    [Migration(1, "Initial scheme")]
    public class Migration_00001_Scheme_Initial : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.TableWithCommonColumns("UserRole")
               .WithColumn("DisplayName").AsString(50).NotNullable()
               .WithColumn("SystemName").AsString(50).NotNullable();

            this.Create.TableWithCommonColumns("User")
                .WithColumn("FirstName").AsString(256).Nullable()
                .WithColumn("LastName").AsString(256).Nullable()
                .WithColumn("Email").AsString(256).NotNullable()
                .WithColumn("Password").AsString(256).NotNullable()
                .WithColumn("Language").AsString().Nullable()
                .WithColumn("UserRoleId").AsGuid().NotNullable()
                    .ForeignKey("UserRole", "Id").OnDeleteOrUpdate(Rule.None)
                .WithColumn("PasswordNonce").AsString(16).NotNullable()
                .WithColumn("ForgotPasswordToken").AsGuid().Nullable()
                .WithColumn("ForgotPasswordTokenExpires").AsDateTime().Nullable()
                .WithColumn("VerifyEmailToken").AsGuid().Nullable()
                .WithColumn("VerifyEmailTokenExpires").AsDateTime().Nullable()
                .WithColumn("ObjectId").AsString(100).Nullable()
                .WithColumn("IsVerified").AsBoolean().NotNullable()
                .WithColumn("IsNativeAuthentication").AsBoolean().WithDefaultValue(true).NotNullable();

            this.Create.TableWithCommonColumns("RefreshToken")
                .WithColumn("Token").AsString(255).NotNullable()
                .WithColumn("Expires").AsDateTime().NotNullable()
                .WithColumn("UserId").AsGuid().NotNullable()
                    .ForeignKey("User", "Id")
                    .OnDeleteOrUpdate(Rule.Cascade);

            this.Create.Table("Log")
                .WithColumn("Id").AsGuid().NotNullable()
                .WithColumn("CreatedOn").AsDateTime().NotNullable()
                .WithColumn("Level").AsString(100).NotNullable()
                .WithColumn("Message").AsMaxString().NotNullable()
                .WithColumn("MachineName").AsString(300).NotNullable()
                .WithColumn("Logger").AsString(300).NotNullable()
                .WithColumn("UserId").AsString(50).Nullable()
                .WithColumn("ExceptionCategory").AsString(256).Nullable()
                .WithColumn("ExceptionType").AsString(256).Nullable()
                .WithColumn("RequestUrl").AsString(400).Nullable()
                .WithColumn("RequestMethod").AsString(20).Nullable()
                .WithColumn("RequestBody").AsMaxString().Nullable();

            this.Create.TableWithCommonColumns("Asset")
                .WithColumn("FileName").AsString(256).Nullable()
                .WithColumn("BlobName").AsString(256).Nullable()
                .WithColumn("Extension").AsString(50).Nullable()
                .WithColumn("FileFormat").AsString(50).Nullable()
                .WithColumn("AssetType").AsInt32().NotNullable()
                .WithColumn("Size").AsInt64().NotNullable()
                .WithColumn("Description").AsString(1000).Nullable();

            this.Create.TableWithCommonColumns("EmailQueue")
                .WithColumn("To").AsString(1000).NotNullable()
                .WithColumn("Message").AsString(10000).Nullable()
                .WithColumn("IsMessageHtml").AsBoolean().NotNullable()
                .WithColumn("Status").AsInt32().NotNullable()
                .WithColumn("Error").AsString(6000).Nullable()
                .WithColumn("Cc").AsString(1000).Nullable()
                .WithColumn("Subject").AsString(1000).Nullable();
        }
    }
}