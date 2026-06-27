namespace GameHubz.DataMigrations
{
    [Migration(48, "Add SwissRoundsCount column for the Swiss tournament format")]
    public class Migration_00048_Add_Swiss_Format : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Tournament: organizer-chosen number of Swiss rounds.
            // Null = auto (ceil(log2(participants))) at bracket generation time.
            Alter.Table("Tournament").AddColumn("SwissRoundsCount").AsInt32().Nullable();
        }
    }
}
