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
                .HasOne(x => x.AwayParticipant)
                .WithMany()
                .HasForeignKey(x => x.AwayParticipantId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<MatchEntity>().ToTable("Match")
                .HasOne(x => x.HomeParticipant)
                .WithMany()
                .HasForeignKey(x => x.HomeParticipantId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<MatchEntity>().ToTable("Match")
                .HasOne(x => x.WinnerParticipant)
                .WithMany()
                .HasForeignKey(x => x.WinnerParticipantId)
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

            modelBuilder.Entity<TournamentStageEntity>().ToTable("TournamentStage").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<TournamentStageEntity>().ToTable("TournamentStage")
                .HasMany(x => x.Matches)
                .WithOne(x => x.TournamentStage)
                .HasForeignKey(x => x.TournamentStageId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<TournamentStageEntity>().ToTable("TournamentStage")
                .HasMany(x => x.TournamentGroups)
                .WithOne(x => x.TournamentStage)
                .HasForeignKey(x => x.TournamentStageId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<TournamentStageEntity>().ToTable("TournamentStage")
                .HasOne(x => x.Tournament)
                .WithMany(x => x.TournamentStages)
                .HasForeignKey(x => x.TournamentId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<TournamentGroupEntity>().ToTable("TournamentGroup").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<TournamentGroupEntity>().ToTable("TournamentGroup")
                .HasOne(x => x.TournamentStage)
                .WithMany(x => x.TournamentGroups)
                .HasForeignKey(x => x.TournamentStageId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<TournamentParticipantEntity>().ToTable("TournamentParticipant").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<TournamentParticipantEntity>().ToTable("TournamentParticipant")
                .HasOne(x => x.Tournament)
                .WithMany(x => x.TournamentParticipants)
                .HasForeignKey(x => x.TournamentId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<TournamentParticipantEntity>().ToTable("TournamentParticipant")
                .HasOne(x => x.User)
                .WithMany(x => x.TournamentParticipants)
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<MatchEntity>().ToTable("Match").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<MatchEntity>().ToTable("Match")
                .HasOne(x => x.TournamentStage)
                .WithMany(x => x.Matches)
                .HasForeignKey(x => x.TournamentStageId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<TournamentEntity>().ToTable("Tournament").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<TournamentEntity>().ToTable("Tournament")
                .HasMany(x => x.TournamentStages)
                .WithOne(x => x.Tournament)
                .HasForeignKey(x => x.TournamentId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<TournamentEntity>().ToTable("Tournament")
                .HasMany(x => x.TournamentParticipants)
                .WithOne(x => x.Tournament)
                .HasForeignKey(x => x.TournamentId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<UserEntity>().ToTable("User").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<UserEntity>().ToTable("User")
                .HasMany(x => x.TournamentParticipants)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<MatchEntity>()
                .HasOne(m => m.HomeParticipant)
                .WithMany()
                .HasForeignKey(m => m.HomeParticipantId)
                .OnDelete(DeleteBehavior.Restrict); // Važno: Sprečava grešku pri brisanju

            modelBuilder.Entity<MatchEntity>()
                .HasOne(m => m.AwayParticipant)
                .WithMany()
                .HasForeignKey(m => m.AwayParticipantId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MatchEntity>()
                .HasOne(m => m.NextMatch)
                .WithMany()
                .HasForeignKey(m => m.NextMatchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MatchEntity>()
                .HasOne(m => m.NextMatchLoserBracket)
                .WithMany()
                .HasForeignKey(m => m.NextMatchLoserBracketId)
                .OnDelete(DeleteBehavior.Restrict);

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