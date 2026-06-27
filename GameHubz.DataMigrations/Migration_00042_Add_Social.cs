namespace GameHubz.DataMigrations
{
    [Migration(42, "Create Friendship, FriendRequest, DirectChat, DirectMessage and UserBlock tables")]
    public class Migration_00042_Add_Social : ForwardOnlyMigration
    {
        public override void Up()
        {
            // ─── Friendship (mutual, normalized: UserAId < UserBId) ──────────────
            Create.Table("Friendship")
                .WithColumn("Id").AsGuid().PrimaryKey()
                .WithColumn("UserAId").AsGuid().NotNullable()
                .WithColumn("UserBId").AsGuid().NotNullable()
                .WithColumn("CreatedOn").AsDateTime().Nullable()
                .WithColumn("ModifiedOn").AsDateTime().Nullable()
                .WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CreatedBy").AsGuid().Nullable()
                .WithColumn("ModifiedBy").AsGuid().Nullable();

            Create.ForeignKey("FK_Friendship_UserA")
                .FromTable("Friendship").ForeignColumn("UserAId")
                .ToTable("User").PrimaryColumn("Id");

            Create.ForeignKey("FK_Friendship_UserB")
                .FromTable("Friendship").ForeignColumn("UserBId")
                .ToTable("User").PrimaryColumn("Id");

            Create.UniqueConstraint("UQ_Friendship_Pair")
                .OnTable("Friendship")
                .Columns("UserAId", "UserBId");

            Create.Index("IX_Friendship_UserAId")
                .OnTable("Friendship").OnColumn("UserAId").Ascending();

            Create.Index("IX_Friendship_UserBId")
                .OnTable("Friendship").OnColumn("UserBId").Ascending();

            // ─── FriendRequest ───────────────────────────────────────────────────
            Create.Table("FriendRequest")
                .WithColumn("Id").AsGuid().PrimaryKey()
                .WithColumn("FromUserId").AsGuid().NotNullable()
                .WithColumn("ToUserId").AsGuid().NotNullable()
                .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("CreatedOn").AsDateTime().Nullable()
                .WithColumn("ModifiedOn").AsDateTime().Nullable()
                .WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CreatedBy").AsGuid().Nullable()
                .WithColumn("ModifiedBy").AsGuid().Nullable();

            Create.ForeignKey("FK_FriendRequest_FromUser")
                .FromTable("FriendRequest").ForeignColumn("FromUserId")
                .ToTable("User").PrimaryColumn("Id");

            Create.ForeignKey("FK_FriendRequest_ToUser")
                .FromTable("FriendRequest").ForeignColumn("ToUserId")
                .ToTable("User").PrimaryColumn("Id");

            Create.Index("IX_FriendRequest_ToUserId_Status")
                .OnTable("FriendRequest")
                .OnColumn("ToUserId").Ascending()
                .OnColumn("Status").Ascending();

            Create.Index("IX_FriendRequest_FromUserId_Status")
                .OnTable("FriendRequest")
                .OnColumn("FromUserId").Ascending()
                .OnColumn("Status").Ascending();

            // ─── DirectChat (1-on-1, normalized: UserAId < UserBId) ──────────────
            Create.Table("DirectChat")
                .WithColumn("Id").AsGuid().PrimaryKey()
                .WithColumn("UserAId").AsGuid().NotNullable()
                .WithColumn("UserBId").AsGuid().NotNullable()
                .WithColumn("LastMessage").AsString(1000).Nullable()
                .WithColumn("LastMessageAt").AsDateTime().Nullable()
                .WithColumn("LastMessageSenderId").AsGuid().Nullable()
                .WithColumn("CreatedOn").AsDateTime().Nullable()
                .WithColumn("ModifiedOn").AsDateTime().Nullable()
                .WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CreatedBy").AsGuid().Nullable()
                .WithColumn("ModifiedBy").AsGuid().Nullable();

            Create.ForeignKey("FK_DirectChat_UserA")
                .FromTable("DirectChat").ForeignColumn("UserAId")
                .ToTable("User").PrimaryColumn("Id");

            Create.ForeignKey("FK_DirectChat_UserB")
                .FromTable("DirectChat").ForeignColumn("UserBId")
                .ToTable("User").PrimaryColumn("Id");

            Create.UniqueConstraint("UQ_DirectChat_Pair")
                .OnTable("DirectChat")
                .Columns("UserAId", "UserBId");

            Create.Index("IX_DirectChat_UserAId_LastMessageAt")
                .OnTable("DirectChat")
                .OnColumn("UserAId").Ascending()
                .OnColumn("LastMessageAt").Descending();

            Create.Index("IX_DirectChat_UserBId_LastMessageAt")
                .OnTable("DirectChat")
                .OnColumn("UserBId").Ascending()
                .OnColumn("LastMessageAt").Descending();

            // ─── DirectMessage ───────────────────────────────────────────────────
            Create.Table("DirectMessage")
                .WithColumn("Id").AsGuid().PrimaryKey()
                .WithColumn("ChatId").AsGuid().NotNullable()
                .WithColumn("SenderId").AsGuid().NotNullable()
                .WithColumn("Content").AsString(4000).NotNullable().WithDefaultValue(string.Empty)
                .WithColumn("IsRead").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("ReadAt").AsDateTime().Nullable()
                .WithColumn("CreatedOn").AsDateTime().Nullable()
                .WithColumn("ModifiedOn").AsDateTime().Nullable()
                .WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CreatedBy").AsGuid().Nullable()
                .WithColumn("ModifiedBy").AsGuid().Nullable();

            Create.ForeignKey("FK_DirectMessage_Chat")
                .FromTable("DirectMessage").ForeignColumn("ChatId")
                .ToTable("DirectChat").PrimaryColumn("Id");

            Create.ForeignKey("FK_DirectMessage_Sender")
                .FromTable("DirectMessage").ForeignColumn("SenderId")
                .ToTable("User").PrimaryColumn("Id");

            Create.Index("IX_DirectMessage_ChatId_CreatedOn")
                .OnTable("DirectMessage")
                .OnColumn("ChatId").Ascending()
                .OnColumn("CreatedOn").Ascending();

            Create.Index("IX_DirectMessage_ChatId_IsRead")
                .OnTable("DirectMessage")
                .OnColumn("ChatId").Ascending()
                .OnColumn("IsRead").Ascending();

            // ─── UserBlock ───────────────────────────────────────────────────────
            Create.Table("UserBlock")
                .WithColumn("Id").AsGuid().PrimaryKey()
                .WithColumn("BlockerId").AsGuid().NotNullable()
                .WithColumn("BlockedId").AsGuid().NotNullable()
                .WithColumn("CreatedOn").AsDateTime().Nullable()
                .WithColumn("ModifiedOn").AsDateTime().Nullable()
                .WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CreatedBy").AsGuid().Nullable()
                .WithColumn("ModifiedBy").AsGuid().Nullable();

            Create.ForeignKey("FK_UserBlock_Blocker")
                .FromTable("UserBlock").ForeignColumn("BlockerId")
                .ToTable("User").PrimaryColumn("Id");

            Create.ForeignKey("FK_UserBlock_Blocked")
                .FromTable("UserBlock").ForeignColumn("BlockedId")
                .ToTable("User").PrimaryColumn("Id");

            Create.UniqueConstraint("UQ_UserBlock_Pair")
                .OnTable("UserBlock")
                .Columns("BlockerId", "BlockedId");

            Create.Index("IX_UserBlock_BlockerId")
                .OnTable("UserBlock").OnColumn("BlockerId").Ascending();

            Create.Index("IX_UserBlock_BlockedId")
                .OnTable("UserBlock").OnColumn("BlockedId").Ascending();
        }
    }
}
