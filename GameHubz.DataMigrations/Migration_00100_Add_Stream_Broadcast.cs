using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(100, "Linked Twitch/Kick channels and in-app broadcast sessions on MatchStream")]
    public class Migration_00100_Add_Stream_Broadcast : ForwardOnlyMigration
    {
        public override void Up()
        {
            Create.TableWithCommonColumns("UserStreamAccount")
                .WithColumn("UserId").AsGuid().NotNullable()
                // SocialType (Twitch / Kick)
                .WithColumn("Platform").AsInt32().NotNullable()
                .WithColumn("ExternalUserId").AsString(64).NotNullable()
                .WithColumn("Login").AsString(256).NotNullable()
                .WithColumn("DisplayName").AsString(256).Nullable()
                // Both tokens encrypted (AES-GCM, base64).
                .WithColumn("AccessToken").AsMaxString().NotNullable()
                .WithColumn("RefreshToken").AsMaxString().NotNullable()
                .WithColumn("AccessTokenExpiresOn").AsDateTime().NotNullable()
                .WithColumn("Scopes").AsString(512).Nullable()
                .WithColumn("NeedsRelink").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("LinkedOn").AsDateTime().NotNullable();

            Create.ForeignKey("FK_UserStreamAccount_User")
                .FromTable("UserStreamAccount").ForeignColumn("UserId")
                .ToTable("User").PrimaryColumn("Id").OnDelete(Rule.Cascade);

            // One channel per platform per user, and one GameHubz account per channel.
            Execute.Sql(@"
                CREATE UNIQUE INDEX ""UX_UserStreamAccount_User_Platform""
                ON ""UserStreamAccount"" (""UserId"", ""Platform"")
                WHERE ""IsDeleted"" = false;
                CREATE UNIQUE INDEX ""UX_UserStreamAccount_Platform_External""
                ON ""UserStreamAccount"" (""Platform"", ""ExternalUserId"")
                WHERE ""IsDeleted"" = false;");

            // Existing rows are all hand-attached streams: Source 0 (Manual), no broadcast session.
            Alter.Table("MatchStream")
                .AddColumn("Source").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("BroadcastTokenHash").AsString(64).Nullable()
                .AddColumn("LastHeartbeatOn").AsDateTime().Nullable()
                .AddColumn("StopRequestedOn").AsDateTime().Nullable()
                .AddColumn("EndReminderSentOn").AsDateTime().Nullable();

            // Heartbeats look the row up by session token; the sweep reads the live broadcasts.
            Execute.Sql(@"
                CREATE UNIQUE INDEX ""UX_MatchStream_BroadcastTokenHash""
                ON ""MatchStream"" (""BroadcastTokenHash"")
                WHERE ""BroadcastTokenHash"" IS NOT NULL;
                CREATE INDEX ""IX_MatchStream_LiveBroadcast""
                ON ""MatchStream"" (""Status"")
                WHERE ""Source"" = 1 AND ""IsDeleted"" = false;");
        }
    }
}
