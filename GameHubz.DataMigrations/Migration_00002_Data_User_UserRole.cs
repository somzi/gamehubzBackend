using GameHubz.Common.Consts;
using GameHubz.Common.Interfaces;

namespace GameHubz.DataMigrations
{
    [Migration(2, "Add application admin user data")]
    public class Migration_00002_Data_User_UserRole : ForwardOnlyMigration
    {
        private readonly IPasswordHasher passwordHasher;

        public Migration_00002_Data_User_UserRole(IPasswordHasher passwordHasher)
        {
            this.passwordHasher = passwordHasher;
        }

        public override void Up()
        {
            var now = DateTime.UtcNow;

            this.InsertUserRole(now, "Application Admin", UserRoleEnum.Admin.ToString(), UserRoles.Admin);
            this.InsertUserRole(now, "Basic user", UserRoleEnum.BasicUser.ToString(), UserRoles.BasicUser);

            this.InsertUser(
                now,
                SystemUsers.AppAdminUserId,
                UserRoles.Admin,
                firstName: "Default",
                lastName: "Administrator",
                email: "admin@GameHubz.rs",
                password: "admin1234",
                passwordNonce: "eo1xf!lZDAFNuX!z",
                true);
        }

        private void InsertUserRole(
            DateTime now,
            string displayName,
            string systemName,
            Guid roleId)
        {
            this.Insert.IntoTable("UserRole").Row(new
            {
                DisplayName = displayName,
                SystemName = systemName,
                Id = roleId,
                CreatedOn = now,
                ModifiedOn = now,
                IsDeleted = false
            });
        }

        private void InsertUser(
            DateTime now,
            Guid userId,
            Guid roleId,
            string firstName,
            string lastName,
            string email,
            string password,
            string passwordNonce,
            bool isVerified)
        {
            this.Insert.IntoTable("User").Row(new
            {
                Id = userId,
                CreatedOn = now,
                ModifiedOn = now,
                IsDeleted = false,
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Password = this.passwordHasher.HashPassword(password, passwordNonce),
                PasswordNonce = passwordNonce,
                UserRoleId = roleId,
                CreatedBy = userId,
                ModifiedBy = userId,
                IsVerified = isVerified
            });
        }
    }
}
