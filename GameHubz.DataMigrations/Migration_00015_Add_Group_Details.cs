namespace GameHubz.DataMigrations
{
    [Migration(15, "Added group details")]
    public class Migration_00015_Add_Group_Details : ForwardOnlyMigration
    {
        public override void Up()
        {
            this.Alter.Table("Tournament")
                .AddColumn("GroupsCount").AsInt32().Nullable()
                .AddColumn("QualifiersPerGroup").AsInt32().Nullable();
        }
    }
}