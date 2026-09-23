using System.Data;

namespace GameHubz.DataMigrations
{
    [Migration(84, "Result verification: per-tournament switch, registered devices, and the verification record")]
    public class Migration_00084_Add_Result_Verification : ForwardOnlyMigration
    {
        public override void Up()
        {
            // Opt-in per tournament. False on every existing row, which is exactly how they behave
            // today: a participant reports a result with no verification step at all.
            Alter.Table("Tournament")
                .AddColumn("RequireResultVerification").AsBoolean().NotNullable().WithDefaultValue(false);

            // A phone an account verifies results from, and the key it keeps behind its biometrics.
            Create.TableWithCommonColumns("UserDevice")
                .WithColumn("UserId").AsGuid().NotNullable()
                .WithColumn("DeviceId").AsGuid().NotNullable()
                // 32-byte HMAC key as hex.
                .WithColumn("KeySecret").AsString(128).NotNullable()
                .WithColumn("KeyIssuedOn").AsDateTime().NotNullable()
                .WithColumn("Platform").AsString(16).NotNullable()
                // Free text reported by the phone — sized generously so an odd vendor string can never
                // fail a registration.
                .WithColumn("DeviceModel").AsString(128).Nullable()
                .WithColumn("DeviceBrand").AsString(64).Nullable()
                .WithColumn("OsVersion").AsString(64).Nullable()
                .WithColumn("AppVersion").AsString(32).Nullable()
                .WithColumn("IsPhysicalDevice").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("PlatformDeviceIdHash").AsString(64).Nullable()
                .WithColumn("AppInstalledOn").AsDateTime().Nullable()
                .WithColumn("LastSeenOn").AsDateTime().NotNullable()
                .WithColumn("LastVerifiedOn").AsDateTime().Nullable();

            // Devices are part of the account: a hard-deleted user takes them along.
            Create.ForeignKey("FK_UserDevice_User")
                .FromTable("UserDevice").ForeignColumn("UserId")
                .ToTable("User").PrimaryColumn("Id")
                .OnDelete(Rule.Cascade);

            // One key per account per phone. Registering again re-issues the key on the same row.
            Execute.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""UX_UserDevice_User_Device""
                ON ""UserDevice"" (""UserId"", ""DeviceId"");");

            // "Which other accounts use this phone?" — asked by the organizer view for every record.
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_UserDevice_DeviceId""
                ON ""UserDevice"" (""DeviceId"");");

            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_UserDevice_PlatformDeviceIdHash""
                ON ""UserDevice"" (""PlatformDeviceIdHash"")
                WHERE ""PlatformDeviceIdHash"" IS NOT NULL;");

            Create.TableWithCommonColumns("MatchResultVerification")
                .WithColumn("MatchId").AsGuid().NotNullable()
                .WithColumn("UserId").AsGuid().NotNullable()
                .WithColumn("UserDeviceId").AsGuid().Nullable()
                .WithColumn("DeviceId").AsGuid().NotNullable()
                .WithColumn("Platform").AsString(16).NotNullable()
                .WithColumn("DeviceModel").AsString(128).Nullable()
                .WithColumn("OsVersion").AsString(64).Nullable()
                .WithColumn("AppVersion").AsString(32).Nullable()
                .WithColumn("IsPhysicalDevice").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("DeviceKeyIssuedOn").AsDateTime().Nullable()
                .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Challenge").AsString(128).NotNullable()
                .WithColumn("ChallengeExpiresOn").AsDateTime().NotNullable()
                .WithColumn("BiometricVerifiedOn").AsDateTime().Nullable()
                .WithColumn("MatchEvidenceId").AsGuid().Nullable()
                .WithColumn("EvidenceUploadedOn").AsDateTime().Nullable()
                .WithColumn("RecordedOn").AsDateTime().Nullable()
                .WithColumn("EvidenceDurationMs").AsInt32().Nullable()
                .WithColumn("EvidenceFileName").AsString(256).Nullable()
                .WithColumn("VerifiedOn").AsDateTime().Nullable()
                .WithColumn("FailureReason").AsString(64).Nullable();

            // A verification is about one match; without it there is nothing left to verify.
            Create.ForeignKey("FK_MatchResultVerification_Match")
                .FromTable("MatchResultVerification").ForeignColumn("MatchId")
                .ToTable("Match").PrimaryColumn("Id")
                .OnDelete(Rule.Cascade);

            Create.ForeignKey("FK_MatchResultVerification_User")
                .FromTable("MatchResultVerification").ForeignColumn("UserId")
                .ToTable("User").PrimaryColumn("Id")
                .OnDelete(Rule.Cascade);

            // The record keeps its own device snapshot, so losing the device row only loses the link.
            Create.ForeignKey("FK_MatchResultVerification_UserDevice")
                .FromTable("MatchResultVerification").ForeignColumn("UserDeviceId")
                .ToTable("UserDevice").PrimaryColumn("Id")
                .OnDelete(Rule.SetNull);

            // Same for the clip: a purge of the evidence row must not take the record with it.
            Create.ForeignKey("FK_MatchResultVerification_MatchEvidence")
                .FromTable("MatchResultVerification").ForeignColumn("MatchEvidenceId")
                .ToTable("MatchEvidence").PrimaryColumn("Id")
                .OnDelete(Rule.SetNull);

            // Every read is "this match" (the panel) or "this player on this match" (the report gate,
            // the attempt limits).
            Execute.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_MatchResultVerification_Match_User""
                ON ""MatchResultVerification"" (""MatchId"", ""UserId"");");
        }
    }
}
