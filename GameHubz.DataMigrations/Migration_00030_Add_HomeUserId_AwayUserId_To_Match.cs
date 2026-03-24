namespace GameHubz.DataMigrations
{
    [Migration(30, "Add HomeUserId and AwayUserId to Match")]
    public class Migration_00030_Add_HomeUserId_AwayUserId_To_Match : ForwardOnlyMigration
    {
        public override void Up()
        {
            Alter.Table("Match").AddColumn("HomeUserId").AsGuid().Nullable();
            Alter.Table("Match").AddColumn("AwayUserId").AsGuid().Nullable();

            Create.ForeignKey("FK_Match_HomeUser")
                .FromTable("Match").ForeignColumn("HomeUserId")
                .ToTable("User").PrimaryColumn("Id");

            Create.ForeignKey("FK_Match_AwayUser")
                .FromTable("Match").ForeignColumn("AwayUserId")
                .ToTable("User").PrimaryColumn("Id");

            // 3. Dodavanje Indeksa za performanse
            Create.Index("IX_Match_HomeUserId")
                .OnTable("Match")
                .OnColumn("HomeUserId")
                .Ascending();

            Create.Index("IX_Match_AwayUserId")
                .OnTable("Match")
                .OnColumn("AwayUserId")
                .Ascending();
        }
    }
}