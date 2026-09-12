namespace GameHubz.DataMigrations
{
    [Migration(81, "Participant-side indexes on Match so the badge / my-matches query stops scanning every active match in the database")]
    public class Migration_00081_Add_Match_Participant_Indexes : ForwardOnlyMigration
    {
        public override void Up()
        {
            // HomeParticipantId / AwayParticipantId have had no index since Match was created, so
            // ActiveForUserPredicate had no participant-side path to drive from. The planner fell
            // back to the only usable index whose leading column it could match — Status — read
            // every Pending / Scheduled / TieBreakRequired match in the database (~9% of the table
            // per call), then nested-loop probed TournamentParticipant by primary key to find out
            // whose match each one was. Measured over the first five months: 1.9 billion index
            // tuple reads here and 278 million PK probes on a 3,100-row table, for 1.96 million
            // calls. It is all in cache so latency held, but the work per call grows with the
            // number of active matches while the number of calls grows with users — the cost is
            // quadratic in growth, which is the reason to fix it now rather than at 10x.
            //
            // Partial on the soft-delete flag: every read goes through the global query filter, so
            // deleted rows are dead weight in the index and their absence keeps it seek-only.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Match_HomeParticipantId""
                    ON ""Match"" (""HomeParticipantId"")
                    WHERE ""IsDeleted"" = FALSE;

                CREATE INDEX IF NOT EXISTS ""IX_Match_AwayParticipantId""
                    ON ""Match"" (""AwayParticipantId"")
                    WHERE ""IsDeleted"" = FALSE;");

            // Narrow support for the organizer admin-help counters, which ask only for matches whose
            // flag is set. An open help request is rare, so this stays near-empty: it costs almost
            // nothing on write (a row enters the index only while flagged) and answers those counts
            // without touching the heap.
            //
            // It does NOT relieve IX_Match_TournamentId_AdminHelpRequested, which was the original
            // intent here. That index reads ~34 entries per scan across 3.6 million scans, and
            // measurement after the fact showed those scans are almost entirely plain
            // per-tournament match enumeration — the planner uses it as the cheapest
            // TournamentId-leading index, and 34 entries is the answer to "which matches are in
            // this tournament", not waste. Match has no dedicated TournamentId index and does not
            // need one while that composite serves the role.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Match_AdminHelp_Open""
                    ON ""Match"" (""TournamentId"")
                    WHERE ""AdminHelpRequested"" AND ""IsDeleted"" = FALSE;");
        }
    }
}
