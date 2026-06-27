namespace GameHubz.DataMigrations
{
    [Migration(57, "Create MatchChatRead — per-user read cursor for match chats (drives unread badges)")]
    public class Migration_00057_Scheme_MatchChatRead : ForwardOnlyMigration
    {
        public override void Up()
        {
            Create.Table("MatchChatRead")
                .WithColumn("Id").AsGuid().PrimaryKey()
                .WithColumn("MatchId").AsGuid().NotNullable()
                .WithColumn("UserId").AsGuid().NotNullable()
                .WithColumn("LastReadAt").AsDateTime().NotNullable()
                .WithColumn("CreatedOn").AsDateTime().Nullable()
                .WithColumn("ModifiedOn").AsDateTime().Nullable()
                .WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CreatedBy").AsGuid().Nullable()
                .WithColumn("ModifiedBy").AsGuid().Nullable();

            Create.ForeignKey("FK_MatchChatRead_Match")
                .FromTable("MatchChatRead").ForeignColumn("MatchId")
                .ToTable("Match").PrimaryColumn("Id");

            Create.ForeignKey("FK_MatchChatRead_User")
                .FromTable("MatchChatRead").ForeignColumn("UserId")
                .ToTable("User").PrimaryColumn("Id");

            // One read cursor per (match, user). Upserts rely on this.
            Create.UniqueConstraint("UQ_MatchChatRead_Match_User")
                .OnTable("MatchChatRead")
                .Columns("MatchId", "UserId");
        }
    }
}
