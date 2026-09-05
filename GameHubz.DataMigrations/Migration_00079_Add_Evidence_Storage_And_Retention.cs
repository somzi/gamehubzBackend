namespace GameHubz.DataMigrations
{
    [Migration(79, "Evidence storage handles + media type, and the tournament end stamp the cleanup sweep retains against")]
    public class Migration_00079_Add_Evidence_Storage_And_Retention : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Deleting an asset needs the provider's own handle. A Cloudinary URL wraps the
            // public_id in folder, version and extension, so reconstructing it at delete time is
            // guesswork on a path that must not guess. Provider is stored alongside it so one
            // sweep can serve two backends the day video moves to cheaper object storage.
            Execute.Sql(@"
                ALTER TABLE ""MatchEvidence"" ADD COLUMN IF NOT EXISTS ""StorageKey"" TEXT NULL;
                ALTER TABLE ""MatchEvidence"" ADD COLUMN IF NOT EXISTS ""Provider"" INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE ""MatchEvidence"" ADD COLUMN IF NOT EXISTS ""MediaType"" INTEGER NOT NULL DEFAULT 0;");

            // Everything uploaded so far is an image (the picker never offered anything else), so
            // the 0 default is already correct for every existing row.

            // Best-effort backfill of the handle for rows that predate the column: take the path
            // after /upload/, drop the version segment and the extension. Uploads carry no
            // incoming transformation in the delivered URL, so there is nothing else in between.
            // A row this misses keeps StorageKey NULL and is simply skipped by the sweep rather
            // than risking a delete against a wrong id.
            Execute.Sql(@"
                UPDATE ""MatchEvidence""
                SET ""StorageKey"" = regexp_replace(
                        regexp_replace(substring(""Url"" from '/upload/(.*)$'), '^v[0-9]+/', ''),
                        '\.[A-Za-z0-9]+$', '')
                WHERE ""StorageKey"" IS NULL
                  AND ""Url"" LIKE '%/upload/%';");

            // When a tournament actually ended. ModifiedOn cannot stand in: any later edit to the
            // row would push the retention window forward for evidence that is long settled.
            Execute.Sql(@"
                ALTER TABLE ""Tournament"" ADD COLUMN IF NOT EXISTS ""EndedOn"" TIMESTAMP NULL;");

            // Backfill already-finished tournaments from their last write. Imprecise, but it is
            // the only signal that exists, and it only ever errs late — worst case the sweep waits
            // longer than it had to.
            Execute.Sql(@"
                UPDATE ""Tournament""
                SET ""EndedOn"" = COALESCE(""ModifiedOn"", ""CreatedOn"")
                WHERE ""EndedOn"" IS NULL
                  AND ""Status"" IN (4, 5, 6);");

            // The sweep scans video rows only. Videos are the minority of evidence, so a partial
            // index keeps it small while letting the scan skip the whole image archive.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_MatchEvidence_Video_Live""
                    ON ""MatchEvidence"" (""MatchId"", ""CreatedOn"")
                    WHERE ""MediaType"" = 1 AND ""IsDeleted"" = FALSE;");
        }
    }
}
