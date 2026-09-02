namespace GameHubz.DataMigrations
{
    [Migration(77, "Backfill User.Language to English so every account has an explicit language")]
    public class Migration_00077_Backfill_User_Language : ForwardOnlyMigration
    {
        public override void Up()
        {
            // The column has existed since migration 1 but nothing ever wrote to it, so every
            // account predating the language picker is NULL — and NULL is not the same thing as
            // English. An unknown recipient language makes a push or e-mail fall back to the
            // language of the request that TRIGGERED it, so one Spanish organiser reporting a
            // result would send Spanish notifications to every English player in the tournament.
            //
            // Stamping English makes the column mean what it says. Anyone who wants Spanish picks
            // it at registration or in Settings, which writes 'es' via POST /api/user/language.
            //
            // The CASE also narrows anything that is not already a bare code: 'es-419' and 'ES'
            // collapse to 'es', and 'sr' — the legacy header default, which never had a resource
            // set and has always rendered English — collapses to 'en' rather than lingering as a
            // third value the app would have to keep special-casing.
            Execute.Sql(@"
                UPDATE ""User""
                SET ""Language"" = CASE
                        WHEN lower(split_part(trim(""Language""), '-', 1)) = 'es' THEN 'es'
                        ELSE 'en'
                    END
                WHERE ""Language"" IS NULL
                   OR ""Language"" NOT IN ('en', 'es');
            ");
        }
    }
}
