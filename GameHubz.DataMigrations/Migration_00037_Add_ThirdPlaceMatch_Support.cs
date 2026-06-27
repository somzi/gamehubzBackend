namespace GameHubz.DataMigrations
{
    [Migration(37, "Add Third Place Match Support")]
    public class Migration_00037_Add_ThirdPlaceMatch_Support : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Tournament: opt-in flag for a third-place play-off. Existing tournaments default to false.
            Alter.Table("Tournament").AddColumn("HasThirdPlaceMatch").AsBoolean().NotNullable().WithDefaultValue(false);

            // TeamMatch: route semi-final losers into the third-place play-off and flag the play-off itself.
            Alter.Table("TeamMatch").AddColumn("NextTeamMatchLoserBracketId").AsGuid().Nullable().ForeignKey("TeamMatch", "Id");
            Alter.Table("TeamMatch").AddColumn("IsThirdPlace").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}