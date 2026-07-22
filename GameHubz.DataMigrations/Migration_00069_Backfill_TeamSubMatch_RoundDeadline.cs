namespace GameHubz.DataMigrations
{
    [Migration(69, "Backfill RoundDeadline on team sub-matches that were created after their round's deadline was set")]
    public class Migration_00069_Backfill_TeamSubMatch_RoundDeadline : ForwardOnlyMigration
    {
        public override void Up()
        {
            // A team tie's games are created only once both teams are known, which is usually
            // after the organizer set that round's deadline — and SetRoundDeadline can only stamp
            // rows that exist at that moment. Those games ended up with RoundDeadline NULL: no
            // deadline shown in the app and no deadline-reminder pushes, while their solo
            // counterparts (all created upfront) were fine.
            //
            // BracketService/TeamMatchService now inherit the round deadline at creation time;
            // this repairs the rows already out there. Scoped to team sub-matches, and only where
            // the same stage+round+group already has a deadline somewhere — a round that never had
            // one is left untouched. Completed matches are harmless to stamp: the reminder sweep
            // only looks at Pending/Scheduled/Live matches with a future deadline.
            Execute.Sql(@"
                UPDATE ""Match"" m
                SET ""RoundDeadline"" = r.""Deadline""
                FROM (
                    SELECT ""TournamentStageId"", ""RoundNumber"", ""TournamentGroupId"",
                           MAX(""RoundDeadline"") AS ""Deadline""
                    FROM ""Match""
                    WHERE ""RoundDeadline"" IS NOT NULL
                    GROUP BY ""TournamentStageId"", ""RoundNumber"", ""TournamentGroupId""
                ) r
                WHERE m.""RoundDeadline"" IS NULL
                  AND m.""TeamMatchId"" IS NOT NULL
                  AND m.""TournamentStageId"" = r.""TournamentStageId""
                  AND m.""RoundNumber"" = r.""RoundNumber""
                  AND m.""TournamentGroupId"" IS NOT DISTINCT FROM r.""TournamentGroupId"";
            ");
        }
    }
}
