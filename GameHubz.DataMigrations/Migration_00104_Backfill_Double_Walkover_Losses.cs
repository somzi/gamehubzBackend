namespace GameHubz.DataMigrations
{
    [Migration(104, "Double walkovers already on the table count as a match played and lost")]
    public class Migration_00104_Backfill_Double_Walkover_Losses : ForwardOnlyMigration
    {
        public override void Up()
        {
            // A league / group / Swiss fixture closed as a double walkover is now a match both sides
            // played and lost (no points, no goals). The code only writes that on the next change to
            // a table, so a group that is already finished would keep its two no-shows a match short
            // for good. This brings the tables that exist today in line.
            //
            // Losses are SET from the fixtures, not incremented, and only for participants who
            // actually have a double walkover — so it gives the same answer whether it runs before
            // or after the API that counts them, and touches nobody else.
            //
            // Match.Status 4 = Completed, 5 = NoShow. TournamentStage.Type 1 = GroupStage,
            // 2 = League, 6 = Swiss.
            Execute.Sql("""
                WITH scoped AS (
                    SELECT m."HomeParticipantId" AS home, m."AwayParticipantId" AS away,
                           m."WinnerParticipantId" AS winner, m."Status" AS status
                    FROM "Match" m
                    JOIN "TournamentStage" s ON s."Id" = m."TournamentStageId"
                    WHERE m."IsDeleted" = false
                      AND m."TeamMatchId" IS NULL
                      AND m."Status" IN (4, 5)
                      AND s."Type" IN (1, 2, 6)
                      AND m."HomeParticipantId" IS NOT NULL
                      AND m."AwayParticipantId" IS NOT NULL
                ),
                sides AS (
                    SELECT home AS pid, away AS opp, winner, status FROM scoped
                    UNION ALL
                    SELECT away, home, winner, status FROM scoped
                ),
                tally AS (
                    SELECT pid,
                           COUNT(*) FILTER (WHERE status = 5) AS no_shows,
                           COUNT(*) FILTER (WHERE status = 4 AND winner = opp) AS defeats
                    FROM sides
                    GROUP BY pid
                )
                UPDATE "TournamentParticipant" p
                SET "Losses" = tally.defeats + tally.no_shows
                FROM tally
                WHERE p."Id" = tally.pid AND tally.no_shows > 0;
                """);

            // Team leagues / groups: a tie is void when every one of its games was closed as a
            // double walkover — completed, no winner, nothing played. TeamMatch.Status 2 = Completed.
            Execute.Sql("""
                WITH ties AS (
                    SELECT tm."HomeTeamParticipantId" AS home, tm."AwayTeamParticipantId" AS away,
                           tm."WinnerTeamParticipantId" AS winner,
                           (tm."WinnerTeamParticipantId" IS NULL
                            AND EXISTS (
                                SELECT 1 FROM "Match" g
                                WHERE g."TeamMatchId" = tm."Id" AND g."IsDeleted" = false)
                            AND NOT EXISTS (
                                SELECT 1 FROM "Match" g
                                WHERE g."TeamMatchId" = tm."Id" AND g."IsDeleted" = false AND g."Status" <> 5)
                           ) AS voided
                    FROM "TeamMatch" tm
                    JOIN "TournamentStage" s ON s."Id" = tm."TournamentStageId"
                    WHERE tm."IsDeleted" = false
                      AND tm."Status" = 2
                      AND s."Type" IN (1, 2)
                      AND tm."HomeTeamParticipantId" IS NOT NULL
                      AND tm."AwayTeamParticipantId" IS NOT NULL
                ),
                sides AS (
                    SELECT home AS pid, away AS opp, winner, voided FROM ties
                    UNION ALL
                    SELECT away, home, winner, voided FROM ties
                ),
                tally AS (
                    SELECT pid,
                           COUNT(*) FILTER (WHERE voided) AS voids,
                           COUNT(*) FILTER (WHERE winner = opp) AS defeats
                    FROM sides
                    GROUP BY pid
                )
                UPDATE "TournamentParticipant" p
                SET "Losses" = tally.defeats + tally.voids
                FROM tally
                WHERE p."Id" = tally.pid AND tally.voids > 0;
                """);
        }
    }
}
