namespace GameHubz.DataMigrations
{
    [Migration(78, "Add availability submission timestamps to Match (who set their slots, and when)")]
    public class Migration_00078_Add_Availability_Submitted_On : ForwardOnlyMigration
    {
        public override void Up()
        {
            // WHICH side set its availability has been answerable since migration 12 (HomeSlotsJson /
            // AwaySlotsJson); WHEN was never recorded. BaseEntity's ModifiedOn is one row-level stamp
            // that every later write to the match overwrites — a reported result, a moved deadline, a
            // format change — so it can be attributed to neither side nor trusted. The organizer
            // deciding "one player tried to schedule, the other ignored it" needs better than that.
            //
            // NULL on every existing row: there is nothing to backfill from, so a match that already
            // carries slots reads as "submitted, time unknown".
            // datetime2 matches RoundDeadline / RoundOpenAt / AdminHelpRequestedOn on this table.
            Alter.Table("Match").AddColumn("HomeSlotsSetOn").AsDateTime2().Nullable();
            Alter.Table("Match").AddColumn("AwaySlotsSetOn").AsDateTime2().Nullable();
        }
    }
}
