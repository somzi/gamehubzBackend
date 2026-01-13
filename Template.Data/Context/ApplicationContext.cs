using Template.DataModels.Domain;
using Microsoft.EntityFrameworkCore;

namespace Template.Data.Context
{
    public class ApplicationContext : DbContext
    {
        public ApplicationContext(DbContextOptions options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            UserConfigurator(modelBuilder);
            UserRoleConfigurator(modelBuilder);
            AssetsConfigurator(modelBuilder);
            EmailQueueConfigurator(modelBuilder);

            modelBuilder.Entity<RefreshTokenEntity>().ToTable("RefreshToken")
                .HasQueryFilter(x => x.IsDeleted == false);

            GeneratedEntityConfigurator(modelBuilder);
        }

        private static void GeneratedEntityConfigurator(ModelBuilder modelBuilder)
        {
            //***********************************************
            //********** GENERATED **************************
            //***********************************************

            // DO NOT DELETE - Generated Configuration Tag
        }

        private static void UserConfigurator(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserEntity>().ToTable("User")
                .HasQueryFilter(x => x.IsDeleted == false)
                .HasMany(x => x.RefreshTokens)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);
        }

        private static void UserRoleConfigurator(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserRoleEntity>().ToTable("UserRole")
                .HasQueryFilter(x => x.IsDeleted == false)
                .HasMany(x => x.Users)
                .WithOne(x => x.UserRole)
                .HasForeignKey(x => x.UserRoleId)
                .HasPrincipalKey(x => x.Id);
        }

        private static void AssetsConfigurator(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AssetEntity>().ToTable("Asset")
                .HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<AssetEntity>()
                .HasOne(x => x.CreatedByUser).WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<AssetEntity>()
                .HasOne(x => x.ModifiedByUser).WithMany()
                .HasForeignKey(x => x.ModifiedBy)
                .HasPrincipalKey(x => x.Id);
        }

        private static void EmailQueueConfigurator(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EmailQueueEntity>().ToTable("EmailQueue")
                .HasQueryFilter(x => x.IsDeleted == false);
        }
    }
}