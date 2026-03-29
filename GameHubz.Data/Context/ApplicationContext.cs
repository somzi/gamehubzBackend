using GameHubz.DataModels.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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
            var utcConverter = new ValueConverter<DateTime, DateTime>(
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties()
                    .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
                {
                    if (property.ClrType == typeof(DateTime))
                    {
                        property.SetValueConverter(utcConverter);
                    }
                    else
                    {
                        property.SetValueConverter(nullableUtcConverter);
                    }
                }
            }

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

            modelBuilder.Entity<HubActivityEntity>().ToTable("HubActivity").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<HubActivityEntity>()
                .HasIndex(x => new { x.HubId, x.CreatedOn });

            modelBuilder.Entity<TournamentEntity>()
                .HasIndex(x => new { x.HubId, x.Status });

            modelBuilder.Entity<TournamentEntity>()
                .HasIndex(x => x.StartDate);

            modelBuilder.Entity<MatchEntity>()
                .HasIndex(x => x.TournamentStageId);

            modelBuilder.Entity<MatchEntity>()
                .HasIndex(x => x.TournamentGroupId);

            modelBuilder.Entity<TournamentParticipantEntity>()
                .HasIndex(x => x.UserId);

            modelBuilder.Entity<TournamentParticipantEntity>()
                .HasIndex(x => x.TournamentGroupId);

            modelBuilder.Entity<MatchEvidenceEntity>().ToTable("MatchEvidence").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<MatchEvidenceEntity>().ToTable("MatchEvidence")
                .HasOne(x => x.Match)
                .WithMany(x => x.MatchEvidences)
                .HasForeignKey(x => x.MatchId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<MatchEntity>().ToTable("Match").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<MatchEntity>().ToTable("Match")
                .HasMany(x => x.MatchEvidences)
                .WithOne(x => x.Match)
                .HasForeignKey(x => x.MatchId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<HubSocialEntity>().ToTable("HubSocial").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<HubSocialEntity>().ToTable("HubSocial")
                .HasOne(x => x.Hub)
                .WithMany(x => x.HubSocials)
                .HasForeignKey(x => x.HubId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<HubEntity>().ToTable("Hub").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<HubEntity>().ToTable("Hub")
                .HasMany(x => x.HubSocials)
                .WithOne(x => x.Hub)
                .HasForeignKey(x => x.HubId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<MatchChatEntity>().ToTable("MatchChat").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<MatchChatEntity>().ToTable("MatchChat")
                .HasOne(x => x.Match)
                .WithMany(x => x.MatchChats)
                .HasForeignKey(x => x.MatchId)
                .HasPrincipalKey(x => x.Id);

            modelBuilder.Entity<MatchEntity>().ToTable("Match").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<MatchEntity>().ToTable("Match")
                .HasMany(x => x.MatchChats)
                .WithOne(x => x.Match)
                .HasForeignKey(x => x.MatchId)
                .HasPrincipalKey(x => x.Id);

            // DO NOT DELETE - Generated Configuration Tag

            TeamTournamentConfigurator(modelBuilder);
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

        private static void TeamTournamentConfigurator(ModelBuilder modelBuilder)
        {
            // TournamentTeam
            modelBuilder.Entity<TournamentTeamEntity>().ToTable("TournamentTeam").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<TournamentTeamEntity>()
                .HasOne(x => x.Tournament)
                .WithMany()
                .HasForeignKey(x => x.TournamentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TournamentTeamEntity>()
                .HasOne(x => x.CaptainUser)
                .WithMany()
                .HasForeignKey(x => x.CaptainUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TournamentTeamEntity>()
                .HasOne(x => x.TournamentParticipant)
                .WithMany()
                .HasForeignKey(x => x.TournamentParticipantId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TournamentTeamEntity>()
                .HasMany(x => x.Members)
                .WithOne(x => x.Team)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            // TournamentTeamMember
            modelBuilder.Entity<TournamentTeamMemberEntity>().ToTable("TournamentTeamMember").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<TournamentTeamMemberEntity>()
                .HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // TeamJoinRequest
            modelBuilder.Entity<TeamJoinRequestEntity>().ToTable("TeamJoinRequest").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<TeamJoinRequestEntity>()
                .HasOne(x => x.Team)
                .WithMany(x => x.JoinRequests)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TeamJoinRequestEntity>()
                .HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TeamJoinRequestEntity>()
                .HasIndex(x => new { x.TeamId, x.Status });

            // TeamMatch
            modelBuilder.Entity<TeamMatchEntity>().ToTable("TeamMatch").HasQueryFilter(x => x.IsDeleted == false);

            modelBuilder.Entity<TeamMatchEntity>()
                .HasOne(x => x.Tournament)
                .WithMany()
                .HasForeignKey(x => x.TournamentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TeamMatchEntity>()
                .HasOne(x => x.TournamentStage)
                .WithMany(x => x.TeamMatches)
                .HasForeignKey(x => x.TournamentStageId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TeamMatchEntity>()
                .HasOne(x => x.HomeTeamParticipant)
                .WithMany()
                .HasForeignKey(x => x.HomeTeamParticipantId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TeamMatchEntity>()
                .HasOne(x => x.AwayTeamParticipant)
                .WithMany()
                .HasForeignKey(x => x.AwayTeamParticipantId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TeamMatchEntity>()
                .HasOne(x => x.NextTeamMatch)
                .WithMany()
                .HasForeignKey(x => x.NextTeamMatchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TeamMatchEntity>()
                .HasMany(x => x.SubMatches)
                .WithOne(x => x.TeamMatch)
                .HasForeignKey(x => x.TeamMatchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MatchEntity>()
                .HasOne(m => m.HomeUser)
                .WithMany()
                .HasForeignKey(m => m.HomeUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MatchEntity>()
                .HasOne(m => m.AwayUser)
                .WithMany()
                .HasForeignKey(m => m.AwayUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TeamMatchEntity>()
                .HasIndex(x => x.TournamentStageId);

            // TournamentParticipant -> Team
            modelBuilder.Entity<TournamentParticipantEntity>()
                .HasOne(x => x.Team)
                .WithOne(x => x.TournamentParticipant)
                .HasForeignKey<TournamentParticipantEntity>(x => x.TeamId)
                .OnDelete(DeleteBehavior.Restrict);

            // Tournament -> WinnerTeam
            modelBuilder.Entity<TournamentEntity>()
                .HasOne(x => x.WinnerTeam)
                .WithMany()
                .HasForeignKey(x => x.WinnerTeamId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}