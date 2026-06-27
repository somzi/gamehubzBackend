namespace GameHubz.DataMigrations
{
    [Migration(53, "Create MatchStream — per-match stream channel + auto-resolved VOD link")]
    public class Migration_00053_Scheme_MatchStream : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Create.TableWithCommonColumns("MatchStream")
                .WithColumn("MatchId").AsGuid().NotNullable()
                .WithColumn("StreamerUserId").AsGuid().NotNullable()
                // SocialType (Twitch/YouTube/Kick)
                .WithColumn("Platform").AsInt32().NotNullable()
                .WithColumn("ChannelHandle").AsString(256).NotNullable().WithDefaultValue("")
                // MatchStreamStatus (Live/Ended)
                .WithColumn("Status").AsInt32().NotNullable()
                .WithColumn("VodUrl").AsString(1024).Nullable()
                .WithColumn("StartedAt").AsDateTime2().Nullable()
                .WithColumn("EndedAt").AsDateTime2().Nullable();

            this.Create.Index("IX_MatchStream_MatchId")
                .OnTable("MatchStream")
                .OnColumn("MatchId").Ascending();
        }
    }
}
