using GameHubz.DataModels.Domain;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Context
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
            HubConfigurator(modelBuilder);

            modelBuilder.Entity<RefreshTokenEntity>().ToTable("RefreshToken")
                .HasQueryFilter(x => x.IsDeleted == false);

            GeneratedEntityConfigurator(modelBuilder);
        }

        private static void HubConfigurator(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<HubEntity>().ToTable("Hub");

            modelBuilder.Entity<HubEntity>()
                .HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);
        }

        private static void GeneratedEntityConfigurator(ModelBuilder modelBuilder)
        {
            //***********************************************
            //********** GENERATED **************************
            //***********************************************

            modelBuilder.Entity<UserHubEntity>().ToTable("UserHub").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<UserHubEntity>().ToTable("UserHub")
                .HasOne(x => x.User)
                .WithMany(x => x.UserHubs)
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<UserHubEntity>().ToTable("UserHub")
                .HasOne(x => x.Hub)
                .WithMany(x => x.UserHubs)
                .HasForeignKey(x => x.HubId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<TournamentEntity>().ToTable("Tournament").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<TournamentEntity>().ToTable("Tournament")
                .HasMany(x => x.TournamentRegistrations)
                .WithOne(x => x.Tournament)
                .HasForeignKey(x => x.TournamentId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<TournamentEntity>().ToTable("Tournament")
                .HasMany(x => x.Matches)
                .WithOne(x => x.Tournament)
                .HasForeignKey(x => x.TournamentId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<TournamentEntity>().ToTable("Tournament")
                .HasOne(x => x.Hub)
                .WithMany(x => x.Tournaments)
                .HasForeignKey(x => x.HubId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<TournamentRegistrationEntity>().ToTable("TournamentRegistration").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<TournamentRegistrationEntity>().ToTable("TournamentRegistration")
                .HasOne(x => x.Tournament)
                .WithMany(x => x.TournamentRegistrations)
                .HasForeignKey(x => x.TournamentId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<TournamentRegistrationEntity>().ToTable("TournamentRegistration")
                .HasOne(x => x.User)
                .WithMany(x => x.TournamentRegistrations)
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<MatchEntity>().ToTable("Match").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<MatchEntity>().ToTable("Match")
                .HasOne(x => x.Tournament)
                .WithMany(x => x.Matches)
                .HasForeignKey(x => x.TournamentId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<MatchEntity>().ToTable("Match")
                .HasOne(x => x.AwayUser)
                .WithMany()
                .HasForeignKey(x => x.AwayUserId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<MatchEntity>().ToTable("Match")
                .HasOne(x => x.HomeUser)
                .WithMany()
                .HasForeignKey(x => x.HomeUserId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<MatchEntity>().ToTable("Match")
                .HasOne(x => x.WinnerUser)
                .WithMany()
                .HasForeignKey(x => x.WinnerUserId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<UserEntity>().ToTable("User").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<UserEntity>().ToTable("User")
                .HasMany(x => x.UserHubs)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<UserEntity>().ToTable("User")
                .HasMany(x => x.TournamentRegistrations)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<HubEntity>().ToTable("Hub").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<HubEntity>().ToTable("Hub")
                .HasMany(x => x.UserHubs)
                .WithOne(x => x.Hub)
                .HasForeignKey(x => x.HubId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<UserSocialEntity>().ToTable("UserSocial").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<UserSocialEntity>().ToTable("UserSocial")
                .HasOne(x => x.User)
                .WithMany(x => x.UserSocials)
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<UserEntity>().ToTable("User").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<UserEntity>().ToTable("User")
                .HasMany(x => x.UserSocials)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);
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