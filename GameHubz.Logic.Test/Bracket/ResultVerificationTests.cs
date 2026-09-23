using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

using GameHubz.Common.Interfaces;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;

namespace GameHubz.Logic.Test.Bracket
{
    // "Verify Result": a registered phone signs a server challenge with its biometric-locked key, the
    // recording of the final score follows inside a window, and only then may that player report. These
    // cover what the feature can get wrong — the signature check and what it binds, the order of the
    // steps, who may verify, and the gate on the result path itself (which must also keep strangers out
    // of a verification-required tournament). SQLite harness, like the other result-path tests.
    [TestFixture]
    internal sealed class ResultVerificationTests
    {
        // ── the proof itself ─────────────────────────────────────────────────────────────────

        [Test]
        public void Proof_SignedWithTheIssuedKey_IsValid()
        {
            string secret = VerificationProof.NewSecret();
            string message = VerificationProof.BuildMessage(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), VerificationProof.NewChallenge());

            Assert.That(VerificationProof.IsValid(secret, message, VerificationProof.Sign(secret, message)), Is.True);
            Assert.That(VerificationProof.IsValid(secret, message, VerificationProof.Sign(secret, message).ToUpperInvariant()), Is.True,
                "hex case is not part of the proof");
        }

        [Test]
        public void Proof_FromAnotherKey_OrForAnotherAttempt_IsRejected()
        {
            string secret = VerificationProof.NewSecret();
            Guid verification = Guid.NewGuid(), match = Guid.NewGuid(), user = Guid.NewGuid(), device = Guid.NewGuid();
            string challenge = VerificationProof.NewChallenge();
            string message = VerificationProof.BuildMessage(verification, match, user, device, challenge);
            string signature = VerificationProof.Sign(secret, message);

            Assert.That(VerificationProof.IsValid(VerificationProof.NewSecret(), message, signature), Is.False, "another phone's key");

            // Every id is inside the signed message, so a signature cannot be lifted onto another attempt.
            Assert.That(VerificationProof.IsValid(secret, VerificationProof.BuildMessage(Guid.NewGuid(), match, user, device, challenge), signature), Is.False);
            Assert.That(VerificationProof.IsValid(secret, VerificationProof.BuildMessage(verification, Guid.NewGuid(), user, device, challenge), signature), Is.False);
            Assert.That(VerificationProof.IsValid(secret, VerificationProof.BuildMessage(verification, match, Guid.NewGuid(), device, challenge), signature), Is.False);
            Assert.That(VerificationProof.IsValid(secret, VerificationProof.BuildMessage(verification, match, user, Guid.NewGuid(), challenge), signature), Is.False);
            Assert.That(VerificationProof.IsValid(secret, VerificationProof.BuildMessage(verification, match, user, device, VerificationProof.NewChallenge()), signature), Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not-hex")]
        [TestCase("abcd")]
        public void Proof_Malformed_IsSimplyRejected(string? signature)
        {
            string secret = VerificationProof.NewSecret();
            Assert.That(VerificationProof.IsValid(secret, "message", signature), Is.False);
        }

        [Test]
        public void Proof_MatchesTheClientsHmac()
        {
            // RFC 4231 test case 2, the vector the phone-side SHA-256 is checked against too: the two
            // implementations have to agree byte for byte or no signature would ever verify.
            string keyHex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes("Jefe")).ToLowerInvariant();
            Assert.That(
                VerificationProof.Sign(keyHex, "what do ya want for nothing?"),
                Is.EqualTo("5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843"));
        }

        // ── the result gate ──────────────────────────────────────────────────────────────────

        [Test]
        public async Task Report_WithoutVerification_IsRefused()
        {
            var (harness, tid, match, home) = await SetUpAsync();

            Assert.That(async () => await harness.NewServiceAsUser(home).UpdateMatchResult(Score(tid, match)),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("Verify your result first"));

            Assert.That(harness.Match(match).Status, Is.Not.EqualTo(MatchStatus.Completed));
        }

        [Test]
        public async Task Report_AfterVerification_GoesThrough()
        {
            var (harness, tid, match, home) = await SetUpAsync();
            var storage = Storage();

            var record = await VerifyAsync(harness, storage, home, match);
            Assert.That(record.Status, Is.EqualTo(MatchVerificationStatus.Verified));

            await harness.NewServiceAsUser(home).UpdateMatchResult(Score(tid, match));

            var reported = harness.Match(match);
            Assert.That(reported.Status, Is.EqualTo(MatchStatus.Completed));
            Assert.That(reported.HomeUserScore, Is.EqualTo(2));
        }

        [Test]
        public async Task Report_ByTheOpponent_StillNeedsTheirOwnVerification()
        {
            var (harness, tid, match, home) = await SetUpAsync();
            var away = harness.ParticipantUserId(harness.Match(match).AwayParticipantId!.Value);
            await harness.DenyManageFor(away, tid);

            await VerifyAsync(harness, Storage(), home, match);

            Assert.That(async () => await harness.NewServiceAsUser(away).UpdateMatchResult(Score(tid, match)),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("Verify your result first"),
                "a verification is personal — the home player's does not cover the away player's report");
        }

        [Test]
        public async Task Report_ByOrganizer_NeedsNoVerification()
        {
            var (harness, tid, match, _) = await SetUpAsync();

            // Default harness token is an Admin: the escape hatch when a phone cannot verify at all.
            await harness.NewService().UpdateMatchResult(Score(tid, match));

            Assert.That(harness.Match(match).Status, Is.EqualTo(MatchStatus.Completed));
        }

        [Test]
        public async Task Report_ByAStranger_IsRefused()
        {
            // Without the setting a non-approval tournament takes a report without asking who sent it;
            // with it, only players can verify, so nobody else reaches the report at all.
            var (harness, tid, match, _) = await SetUpAsync();
            var outsider = Guid.NewGuid();
            await harness.DenyManageFor(outsider, tid);

            Assert.That(async () => await harness.NewServiceAsUser(outsider).UpdateMatchResult(Score(tid, match)),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("participant"));
        }

        [Test]
        public async Task Proposal_InApprovalMode_NeedsVerificationToo()
        {
            var (harness, tid, match, home) = await SetUpAsync(requireResultApproval: true);

            Assert.That(async () => await harness.NewServiceAsUser(home).UpdateMatchResult(Score(tid, match)),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("Verify your result first"));

            await VerifyAsync(harness, Storage(), home, match);
            await harness.NewServiceAsUser(home).UpdateMatchResult(Score(tid, match));

            Assert.That(harness.Match(match).ProposedByUserId, Is.EqualTo(home), "the verified player's proposal is on file");
        }

        [Test]
        public async Task Report_WhenNotRequired_IsUnchanged()
        {
            var (harness, tid, match, home) = await SetUpAsync(requireVerification: false);

            await harness.NewServiceAsUser(home).UpdateMatchResult(Score(tid, match));

            Assert.That(harness.Match(match).Status, Is.EqualTo(MatchStatus.Completed));
        }

        // ── the steps, and their order ───────────────────────────────────────────────────────

        [Test]
        public async Task Start_WhenTheTournamentDoesNotVerify_IsRefused()
        {
            var (harness, _, match, home) = await SetUpAsync(requireVerification: false);
            var service = harness.NewMatchVerificationServiceAsUser(home, Storage().Object);
            var device = await service.RegisterDevice(Phone());

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, Storage().Object)
                    .Start(match, new StartMatchVerificationRequest { DeviceId = device.DeviceId }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("doesn't use result verification"));
        }

        [Test]
        public async Task Start_ByANonParticipant_IsRefused()
        {
            var (harness, _, match, _) = await SetUpAsync();
            var outsider = Guid.NewGuid();
            var device = await harness.NewMatchVerificationServiceAsUser(outsider, Storage().Object).RegisterDevice(Phone());

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(outsider, Storage().Object)
                    .Start(match, new StartMatchVerificationRequest { DeviceId = device.DeviceId }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("participant"));
        }

        [Test]
        public async Task Start_FromAPhoneThisAccountNeverRegistered_IsRefused()
        {
            var (harness, _, match, home) = await SetUpAsync();

            // Its own exception type: the controller answers it with 409, the phone's cue to register again.
            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, Storage().Object)
                    .Start(match, new StartMatchVerificationRequest { DeviceId = Guid.NewGuid() }),
                Throws.TypeOf<VerificationDeviceUnknownException>().With.Message.Contains("isn't registered"));
        }

        [Test]
        public async Task WrongSignature_FailsTheAttempt_AndIsKept()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var device = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());
            var start = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .Start(match, new StartMatchVerificationRequest { DeviceId = device.DeviceId });

            // Signed with a key this phone was never issued.
            string forged = VerificationProof.Sign(VerificationProof.NewSecret(), start.Message);

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .SubmitBiometricProof(start.VerificationId, new SubmitBiometricProofRequest { Signature = forged }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("didn't match"));

            var row = Verification(harness, start.VerificationId);
            Assert.That(row.Status, Is.EqualTo(MatchVerificationStatus.Failed), "the failed proof is recorded, not discarded");
            Assert.That(row.FailureReason, Is.EqualTo("signatureMismatch"));

            // And the attempt is dead: neither a second proof nor a clip can revive it.
            string genuine = VerificationProof.Sign(device.Secret, start.Message);
            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .SubmitBiometricProof(start.VerificationId, new SubmitBiometricProofRequest { Signature = genuine }),
                Throws.TypeOf<BusinessRuleException>());
            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest()),
                Throws.TypeOf<BusinessRuleException>());
        }

        [Test]
        public async Task ExpiredChallenge_CannotBeAnswered()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var device = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());
            var start = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .Start(match, new StartMatchVerificationRequest { DeviceId = device.DeviceId });

            using (var ctx = harness.ReadContext())
            {
                var row = ctx.Set<MatchResultVerificationEntity>().Single(v => v.Id == start.VerificationId);
                row.ChallengeExpiresOn = DateTime.UtcNow.AddSeconds(-1);
                await ctx.SaveChangesAsync();
            }

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .SubmitBiometricProof(start.VerificationId, new SubmitBiometricProofRequest
                    {
                        Signature = VerificationProof.Sign(device.Secret, start.Message),
                    }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("expired"));
        }

        [Test]
        public async Task Recording_BeforeTheBiometricProof_IsRefused()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var device = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());
            var start = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .Start(match, new StartMatchVerificationRequest { DeviceId = device.DeviceId });

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest()),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("Face ID"));

            storage.Verify(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never,
                "nothing is stored for an attempt that has not proven anything");
        }

        [Test]
        public async Task Recording_MustBeAVideo()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var (start, _) = await ProveAsync(harness, storage, home, match);

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .AttachEvidence(start.VerificationId, Clip("image/jpeg"), new AttachVerificationEvidenceRequest()),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("video"));
        }

        [Test]
        public async Task Recording_AfterTheWindow_IsRefused()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var (start, _) = await ProveAsync(harness, storage, home, match);

            using (var ctx = harness.ReadContext())
            {
                var row = ctx.Set<MatchResultVerificationEntity>().Single(v => v.Id == start.VerificationId);
                row.BiometricVerifiedOn = DateTime.UtcNow.AddHours(-1);
                await ctx.SaveChangesAsync();
            }

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest()),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("expired"));
        }

        [Test]
        public async Task Recording_IsStoredAsMatchEvidence_AndLinked()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();

            var record = await VerifyAsync(harness, storage, home, match);

            var row = Verification(harness, record.Id!.Value);
            Assert.That(row.MatchEvidenceId, Is.Not.Null);
            Assert.That(row.BiometricVerifiedOn, Is.Not.Null);
            Assert.That(row.VerifiedOn, Is.Not.Null);
            Assert.That(row.DeviceModel, Is.EqualTo("iPhone 15 Pro"), "the device is snapshotted onto the record");

            using var ctx = harness.ReadContext();
            var evidence = ctx.Set<MatchEvidenceEntity>().Single(e => e.Id == row.MatchEvidenceId);
            Assert.That(evidence.MatchId, Is.EqualTo(match), "the clip lands in the match's ordinary evidence");
            Assert.That(evidence.MediaType, Is.EqualTo(EvidenceMediaType.Video));
        }

        [Test]
        public async Task Recording_Retried_IsNotStoredTwice()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var (start, _) = await ProveAsync(harness, storage, home, match);

            await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest());

            // The answer was lost on the way back and the phone sends the clip again.
            var again = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest());

            Assert.That(again.Status, Is.EqualTo(MatchVerificationStatus.Verified));
            storage.Verify(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Test]
        public async Task SomeoneElsesAttempt_IsNotTheirsToAnswer()
        {
            var (harness, tid, match, home) = await SetUpAsync();
            var storage = Storage();
            var device = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());
            var start = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .Start(match, new StartMatchVerificationRequest { DeviceId = device.DeviceId });

            var away = harness.ParticipantUserId(harness.Match(match).AwayParticipantId!.Value);

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(away, storage.Object)
                    .SubmitBiometricProof(start.VerificationId, new SubmitBiometricProofRequest
                    {
                        Signature = VerificationProof.Sign(device.Secret, start.Message),
                    }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("not found"));
        }

        [Test]
        public async Task ReRegistering_ReissuesTheKey_OnTheSameDevice()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var first = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());

            // Biometrics changed on the phone, the locked key is gone: the phone registers again.
            var second = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .RegisterDevice(Phone(first.DeviceId));

            Assert.That(second.DeviceId, Is.EqualTo(first.DeviceId), "same phone, same installation id");
            Assert.That(second.Secret, Is.Not.EqualTo(first.Secret), "but a new key");

            var start = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .Start(match, new StartMatchVerificationRequest { DeviceId = second.DeviceId });

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .SubmitBiometricProof(start.VerificationId, new SubmitBiometricProofRequest
                    {
                        Signature = VerificationProof.Sign(first.Secret, start.Message),
                    }),
                Throws.TypeOf<BusinessRuleException>(), "the old key stops working the moment a new one is issued");
        }

        // ── the panel ────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Panel_ForAPlayer_BlocksTheReportUntilVerified()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();

            var before = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).GetPanel(match);
            Assert.That(before.Required, Is.True);
            Assert.That(before.CanVerify, Is.True);
            Assert.That(before.ReportBlocked, Is.True);
            Assert.That(before.Records, Has.Count.EqualTo(2), "both players are listed, verified or not");
            Assert.That(before.Records.All(r => r.Id == null), Is.True);

            await VerifyAsync(harness, storage, home, match);

            var after = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).GetPanel(match);
            Assert.That(after.ReportBlocked, Is.False);
            Assert.That(after.Mine, Is.Not.Null);
            Assert.That(after.Mine!.Device, Is.Not.Null, "a player sees their own device");
            Assert.That(after.Mine.Flags, Is.Empty, "flags are an organizer's view");
        }

        [Test]
        public async Task Panel_ForTheOpponent_HidesTheOtherPlayersDevice()
        {
            var (harness, tid, match, home) = await SetUpAsync();
            var away = harness.ParticipantUserId(harness.Match(match).AwayParticipantId!.Value);
            await harness.DenyManageFor(away, tid);

            await VerifyAsync(harness, Storage(), home, match);

            var panel = await harness.NewMatchVerificationServiceAsUser(away, Storage().Object).GetPanel(match);
            var homeRecord = panel.Records.Single(r => r.UserId == home);

            Assert.That(homeRecord.Status, Is.EqualTo(MatchVerificationStatus.Verified));
            Assert.That(homeRecord.Evidence, Is.Not.Null, "the clip is evidence both sides can see");
            Assert.That(homeRecord.Device, Is.Null, "but the phone it came from is not the opponent's business");
        }

        [Test]
        public async Task Panel_ForAnOrganizer_DoesNotFlagTheFirstPhoneOrItsInitialKey()
        {
            var (harness, _, match, home) = await SetUpAsync();

            await VerifyAsync(harness, Storage(), home, match);

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, Storage().Object, role: "Admin")
                .GetPanel(match);

            Assert.That(panel.IsManager, Is.True);
            Assert.That(panel.ReportBlocked, Is.False, "organizers are never blocked");

            var homeRecord = panel.Records.Single(r => r.UserId == home);
            Assert.That(homeRecord.Device, Is.Not.Null);
            Assert.That(homeRecord.Device!.DeviceModel, Is.EqualTo("iPhone 15 Pro"));
            Assert.That(homeRecord.Device.AccountDeviceCount, Is.EqualTo(1));
            Assert.That(homeRecord.Flags, Is.Empty, "the first phone and its initial key are expected");
        }

        [TestCase("ios", "vendor-id-1")]
        [TestCase("android", "different-android-id")]
        [TestCase("android", null)]
        [TestCase("android", "")]
        public async Task Panel_FlagsASecondPhone_WhenNoAndroidReinstallCanBeRecognized(string platform, string? platformId)
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var original = Phone();
            original.Platform = platform;
            var first = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(original);
            await SetDeviceRegisteredOn(harness, first.DeviceId, DateTime.UtcNow.AddDays(-7));

            var request = Phone();
            request.Platform = platform;
            request.PlatformDeviceId = platformId;
            var second = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(request);
            await VerifyAsync(harness, storage, home, match, second);

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object, role: "Admin")
                .GetPanel(match);
            var record = panel.Records.Single(r => r.UserId == home);

            Assert.That(record.Device!.AccountDeviceCount, Is.EqualTo(2));
            Assert.That(record.Flags, Does.Contain("newDevice"));
            Assert.That(record.Flags, Does.Not.Contain("freshKey"), "the new phone's initial key needs no separate warning");
        }

        [Test]
        public async Task Panel_RegisteringAnotherPhone_DoesNotRetroactivelyFlagTheFirst()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var first = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());
            await SetDeviceRegisteredOn(harness, first.DeviceId, DateTime.UtcNow.AddHours(-1));
            await VerifyAsync(harness, storage, home, match, first);

            await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object, role: "Admin")
                .GetPanel(match);
            var record = panel.Records.Single(r => r.UserId == home);

            Assert.That(record.Device!.AccountDeviceCount, Is.EqualTo(2));
            Assert.That(record.Flags, Is.Empty, "the first phone remains the first even after another is registered");
        }

        [Test]
        public async Task Panel_AndroidReinstall_UsesTheKnownPhonesHistory_AndPreservesKeysAndRecords()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var other = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());
            await SetDeviceRegisteredOn(harness, other.DeviceId, DateTime.UtcNow.AddDays(-14));

            var request = Phone();
            request.Platform = "android";
            request.PlatformDeviceId = "known-android-id";
            var original = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(request);
            var firstSeen = DateTime.UtcNow.AddDays(-7);
            await SetDeviceRegisteredOn(harness, original.DeviceId, firstSeen);
            // An attempt from before the reinstall — proven, not completed: one verification per player
            // per match, and the completed one below is the reinstalled phone's.
            var (originalAttempt, _) = await ProveAsync(harness, storage, home, match, original);

            // Android clears the installation id and key on reinstall, but reports the same Android ID.
            var reinstalled = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(request);
            await VerifyAsync(harness, storage, home, match, reinstalled);

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object, role: "Admin")
                .GetPanel(match);
            var record = panel.Records.Single(r => r.UserId == home);

            Assert.That(record.Device!.DeviceId, Is.EqualTo(reinstalled.DeviceId));
            Assert.That(record.Device.FirstSeenOn, Is.EqualTo(firstSeen));
            Assert.That(record.Device.AccountDeviceCount, Is.EqualTo(2), "three installations belong to two phones");
            Assert.That(record.Flags, Does.Not.Contain("newDevice"));
            Assert.That(record.Flags, Does.Contain("freshKey"), "the reinstall really did issue a new key");

            using var ctx = harness.ReadContext();
            var originalRow = ctx.Set<UserDeviceEntity>().Single(d => d.UserId == home && d.DeviceId == original.DeviceId);
            var reinstalledRow = ctx.Set<UserDeviceEntity>().Single(d => d.UserId == home && d.DeviceId == reinstalled.DeviceId);
            Assert.That(reinstalledRow.Id, Is.Not.EqualTo(originalRow.Id));
            Assert.That(originalRow.KeySecret, Is.EqualTo(original.Secret), "recognition must not rotate another installation's key");
            Assert.That(reinstalledRow.KeySecret, Is.EqualTo(reinstalled.Secret));
            var storedRecord = ctx.Set<MatchResultVerificationEntity>().Single(v => v.Id == originalAttempt.VerificationId);
            Assert.That(storedRecord.UserDeviceId, Is.EqualTo(originalRow.Id), "a past attempt keeps its original installation");
            Assert.That(storedRecord.DeviceKeyIssuedOn, Is.EqualTo(firstSeen));
        }

        [TestCase("same-android-id", 1)]
        [TestCase(null, 2)]
        public async Task Panel_AndroidRegistrations_GroupOnlyWhenBothHaveAMatchingId(string? platformId, int expectedPhones)
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var request = Phone();
            request.Platform = "android";
            request.PlatformDeviceId = platformId;
            var first = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(request);
            await SetDeviceRegisteredOn(harness, first.DeviceId, DateTime.UtcNow.AddMinutes(-5));
            var second = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(request);
            await VerifyAsync(harness, storage, home, match, second);

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object, role: "Admin")
                .GetPanel(match);
            var record = panel.Records.Single(r => r.UserId == home);

            Assert.That(record.Device!.AccountDeviceCount, Is.EqualTo(expectedPhones));
            Assert.That(record.Flags.Contains("newDevice"), Is.EqualTo(expectedPhones == 2));
            Assert.That(record.Flags, Does.Not.Contain("freshKey"), "a recently registered phone still has its initial-key grace period");
        }

        [Test]
        public async Task Panel_AndroidIdFromAnotherAccount_DoesNotMakeANewPhoneFamiliar()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var away = harness.ParticipantUserId(harness.Match(match).AwayParticipantId!.Value);
            var first = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());
            await SetDeviceRegisteredOn(harness, first.DeviceId, DateTime.UtcNow.AddDays(-14));

            var request = Phone();
            request.Platform = "android";
            request.PlatformDeviceId = "shared-android-id";
            var awayPhone = await harness.NewMatchVerificationServiceAsUser(away, storage.Object).RegisterDevice(request);
            await SetDeviceRegisteredOn(harness, awayPhone.DeviceId, DateTime.UtcNow.AddDays(-7));
            var homePhone = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(request);
            await VerifyAsync(harness, storage, home, match, homePhone);

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object, role: "Admin")
                .GetPanel(match);
            var record = panel.Records.Single(r => r.UserId == home);

            Assert.That(record.Flags, Does.Contain("newDevice"), "recognition is scoped to this account's history");
            Assert.That(record.Flags, Does.Contain("sharedDevice"));
            Assert.That(record.Device!.OtherAccountsOnDevice, Is.EqualTo(1));
            Assert.That(record.Device.AccountDeviceCount, Is.EqualTo(2));

            // Matched by the Android ID alone — two installations, two ids — and still named.
            var other = record.Device.OtherAccounts.Single();
            Assert.That(other.UserId, Is.EqualTo(away));
            Assert.That(other.CameFirst, Is.True, "the opponent's account was on this phone a week earlier");
        }

        [Test]
        public async Task Panel_AndroidId_DoesNotMatchAnIosRegistration()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var first = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());
            await SetDeviceRegisteredOn(harness, first.DeviceId, DateTime.UtcNow.AddDays(-7));
            var request = Phone();
            request.Platform = "android";
            var second = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(request);
            await VerifyAsync(harness, storage, home, match, second);

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object, role: "Admin")
                .GetPanel(match);
            var record = panel.Records.Single(r => r.UserId == home);

            Assert.That(record.Flags, Does.Contain("newDevice"));
            Assert.That(record.Device!.AccountDeviceCount, Is.EqualTo(2));
        }

        [Test]
        public async Task Panel_FlagsAPhoneSharedBetweenAccounts()
        {
            var (harness, tid, match, home) = await SetUpAsync();
            var storage = Storage();
            var away = harness.ParticipantUserId(harness.Match(match).AwayParticipantId!.Value);
            await harness.DenyManageFor(away, tid);

            var homeDevice = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());
            // The opponent registers from the very same installation.
            await harness.NewMatchVerificationServiceAsUser(away, storage.Object).RegisterDevice(Phone(homeDevice.DeviceId));

            await VerifyAsync(harness, storage, home, match, homeDevice);

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object, role: "Admin")
                .GetPanel(match);

            var homeRecord = panel.Records.Single(r => r.UserId == home);
            Assert.That(homeRecord.Device!.OtherAccountsOnDevice, Is.EqualTo(1));
            Assert.That(homeRecord.Flags, Does.Contain("sharedDevice"));
        }

        [Test]
        public void Flags_OldRecording_IsMeasuredAgainstTheVerification()
        {
            var now = DateTime.UtcNow;
            var row = new MatchResultVerificationEntity
            {
                CreatedOn = now,
                VerifiedOn = now,
                IsPhysicalDevice = true,
                RecordedOn = now.AddDays(-2),
            };

            Assert.That(GameHubz.Logic.Services.MatchVerificationService.BuildFlags(row, device: null), Does.Contain("oldRecording"));

            row.RecordedOn = now.AddMinutes(-3);
            Assert.That(GameHubz.Logic.Services.MatchVerificationService.BuildFlags(row, device: null), Does.Not.Contain("oldRecording"));

            row.IsPhysicalDevice = false;
            Assert.That(GameHubz.Logic.Services.MatchVerificationService.BuildFlags(row, device: null), Does.Contain("emulator"));
        }

        // ── review follow-ups ────────────────────────────────────────────────────────────────

        [Test]
        public async Task Recording_UploadedTwiceAtOnce_IsStoredOnce()
        {
            // The phone gave up waiting at its timeout and retried while the server was still storing the
            // first clip. The finished-attempt check cannot see that; the upload claim has to.
            var (harness, _, match, home) = await SetUpAsync();

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<StoredAsset?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var storage = new Mock<IStorageService>();
            storage
                .Setup(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(() =>
                {
                    entered.TrySetResult();
                    return release.Task;
                });

            var (start, _) = await ProveAsync(harness, storage, home, match);

            // First upload: holds the claim and is parked inside the storage call.
            var first = harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest());
            await entered.Task;

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest()),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("still uploading"));

            release.SetResult(new StoredAsset("https://cdn.test/clip.mp4", "clip-key", StorageProviderType.Cloudinary));
            var record = await first;

            Assert.That(record.Status, Is.EqualTo(MatchVerificationStatus.Verified));
            storage.Verify(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<MatchEvidenceEntity>().Count(e => e.MatchId == match), Is.EqualTo(1));
        }

        [Test]
        public async Task Recording_AfterAFailedUpload_CanBeRetriedAtOnce()
        {
            // A failed upload hands its claim back; the retry must not wait out the stale window.
            var (harness, _, match, home) = await SetUpAsync();

            var storage = new Mock<IStorageService>();
            storage
                .SetupSequence(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("storage down"))
                .ReturnsAsync(new StoredAsset("https://cdn.test/clip.mp4", "clip-key", StorageProviderType.Cloudinary));

            var (start, _) = await ProveAsync(harness, storage, home, match);

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest()),
                Throws.TypeOf<InvalidOperationException>());

            var retried = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest());

            Assert.That(retried.Status, Is.EqualTo(MatchVerificationStatus.Verified));
        }

        [Test]
        public async Task KeyReissuedMidAttempt_IsTheKeyOnRecord_AndIsFlagged()
        {
            // The OS dropped the locked key — faces or fingers changed on the phone — so the app registers
            // again between the challenge and the proof. That fresh key is exactly what an organizer
            // should see flagged, so the record must carry it rather than the one from before.
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var device = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());

            // A phone this account has had for a week.
            using (var ctx = harness.ReadContext())
            {
                var row = ctx.Set<UserDeviceEntity>().Single(d => d.DeviceId == device.DeviceId);
                row.CreatedOn = DateTime.UtcNow.AddDays(-7);
                row.KeyIssuedOn = DateTime.UtcNow.AddDays(-7);
                await ctx.SaveChangesAsync();
            }

            var start = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .Start(match, new StartMatchVerificationRequest { DeviceId = device.DeviceId });
            var reissued = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .RegisterDevice(Phone(device.DeviceId));
            await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .SubmitBiometricProof(start.VerificationId, new SubmitBiometricProofRequest
                {
                    Signature = VerificationProof.Sign(reissued.Secret, start.Message),
                });
            await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest());

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object, role: "Admin")
                .GetPanel(match);
            var homeRecord = panel.Records.Single(r => r.UserId == home);

            Assert.That(homeRecord.Flags, Does.Contain("freshKey"));
            Assert.That(homeRecord.Flags, Does.Not.Contain("newDevice"), "the phone itself is a week old");
        }

        [Test]
        public async Task Start_RecordsThePhoneAsItIsNow()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var device = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());

            // Weeks later: the OS and the app have both updated, the key has not changed.
            var start = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .Start(match, new StartMatchVerificationRequest
                {
                    DeviceId = device.DeviceId,
                    OsVersion = "18.2",
                    AppVersion = "2.20.0",
                });

            var row = Verification(harness, start.VerificationId);
            Assert.That(row.OsVersion, Is.EqualTo("18.2"));
            Assert.That(row.AppVersion, Is.EqualTo("2.20.0"));
            Assert.That(row.DeviceModel, Is.EqualTo("iPhone 15 Pro"), "a field the phone did not send keeps what is stored");

            using var ctx = harness.ReadContext();
            var stored = ctx.Set<UserDeviceEntity>().Single(d => d.DeviceId == device.DeviceId && d.UserId == home);
            Assert.That(stored.OsVersion, Is.EqualTo("18.2"), "the device row moves on with the phone");
        }

        [Test]
        public async Task KeyReissues_AreLimited_EvenOnOnePhone()
        {
            // Counting device rows let one phone re-issue its key without end: every re-issue rewrites
            // the same row. The limit counts issuances.
            var (harness, _, _, home) = await SetUpAsync();
            var storage = Storage();

            var first = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());
            for (int i = 1; i < 10; i++)
            {
                await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone(first.DeviceId));
            }

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .RegisterDevice(Phone(first.DeviceId)),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("Too many"));
        }

        [Test]
        public async Task OvertakenUpload_DoesNotOverwriteTheTakeover()
        {
            // The first request sat in the upload past the stale window; a retry took the claim over,
            // stored its clip and completed the attempt. When the first one finally comes back it must
            // lose cleanly — no second evidence row, no overwritten record, its own clip deleted.
            var (harness, _, match, home) = await SetUpAsync();

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<StoredAsset?>(TaskCreationOptions.RunContinuationsAsynchronously);
            int uploads = 0;

            var storage = new Mock<IStorageService>();
            storage
                .Setup(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(() =>
                {
                    if (Interlocked.Increment(ref uploads) == 1)
                    {
                        entered.TrySetResult();
                        return releaseFirst.Task;
                    }
                    return Task.FromResult<StoredAsset?>(
                        new StoredAsset("https://cdn.test/takeover.mp4", "takeover-key", StorageProviderType.Cloudinary));
                });

            var (start, _) = await ProveAsync(harness, storage, home, match);

            var first = harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest());
            await entered.Task;

            // The first request has been uploading for longer than the stale window.
            using (var ctx = harness.ReadContext())
            {
                var row = ctx.Set<MatchResultVerificationEntity>().Single(v => v.Id == start.VerificationId);
                row.EvidenceUploadClaimedOn = DateTime.UtcNow.AddMinutes(-10);
                await ctx.SaveChangesAsync();
            }

            var takeover = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest());
            Assert.That(takeover.Status, Is.EqualTo(MatchVerificationStatus.Verified));

            releaseFirst.SetResult(new StoredAsset("https://cdn.test/overtaken.mp4", "overtaken-key", StorageProviderType.Cloudinary));
            var late = await first;

            Assert.That(late.Evidence?.Url, Is.EqualTo("https://cdn.test/takeover.mp4"),
                "the late request answers with the attempt as the takeover completed it");

            using var read = harness.ReadContext();
            var evidence = read.Set<MatchEvidenceEntity>().Where(e => e.MatchId == match).ToList();
            Assert.That(evidence, Has.Count.EqualTo(1), "the overtaken request's row rolled back");
            Assert.That(evidence[0].Url, Is.EqualTo("https://cdn.test/takeover.mp4"));

            var verification = read.Set<MatchResultVerificationEntity>().Single(v => v.Id == start.VerificationId);
            Assert.That(verification.MatchEvidenceId, Is.EqualTo(evidence[0].Id), "the record still points at the takeover's clip");

            storage.Verify(s => s.DeleteAsync("overtaken-key", EvidenceMediaType.Video, It.IsAny<CancellationToken>()), Times.Once,
                "and the overtaken clip is taken back off storage");
        }

        [Test]
        public async Task KeyIssueLimit_IsDecidedByTheAtomicCounter()
        {
            // Ten other registrations took their slots in the same instant — the value the increment
            // hands back is what decides, and nothing is issued past it. A read-then-increment would have
            // seen an old count here and let this one through.
            var (harness, _, _, home) = await SetUpAsync();

            var cache = new Mock<ICacheService>();
            cache.Setup(c => c.GetCounterAsync(It.IsAny<string>())).ReturnsAsync(0);
            cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(11);

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, Storage().Object, cache: cache.Object)
                    .RegisterDevice(Phone()),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("Too many"));

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<UserDeviceEntity>().Any(d => d.UserId == home), Is.False, "no key was minted past the limit");
        }

        [Test]
        public async Task SwitchingVerificationOff_KeepsPastRecordsVisible()
        {
            var (harness, tid, match, home) = await SetUpAsync();
            await VerifyAsync(harness, Storage(), home, match);

            using (var ctx = harness.ReadContext())
            {
                ctx.Set<TournamentEntity>().Single(t => t.Id == tid).RequireResultVerification = false;
                await ctx.SaveChangesAsync();
            }

            var details = await harness.NewMatchServiceAsUser(home).GetWithEvidence(match);
            Assert.That(details.RequireResultVerification, Is.False);
            Assert.That(details.HasResultVerifications, Is.True, "the app still has a reason to load the records");

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, Storage().Object, role: "Admin")
                .GetPanel(match);
            Assert.That(panel.Required, Is.False);
            Assert.That(panel.Records.Any(r => r.UserId == home && r.Status == MatchVerificationStatus.Verified), Is.True);
        }

        // ── one verification per player; OS updates; who else is on the phone ────────────────

        [Test]
        public async Task Start_AfterThePlayerVerified_IsRefused_AndThePanelAsksNothingMore()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var device = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());
            await VerifyAsync(harness, storage, home, match, device);

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .Start(match, new StartMatchVerificationRequest { DeviceId = device.DeviceId }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("already verified"));

            // Refused before the device is looked up: an unknown phone is not sent off to register a
            // key (and spend one of the day's issuances) only to hear the same answer.
            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .Start(match, new StartMatchVerificationRequest { DeviceId = Guid.NewGuid() }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("already verified"));

            var panel = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).GetPanel(match);
            Assert.That(panel.CanVerify, Is.False, "nothing is left for this player to do");
            Assert.That(panel.ReportBlocked, Is.False);
            Assert.That(panel.Mine?.Status, Is.EqualTo(MatchVerificationStatus.Verified));
        }

        [Test]
        public async Task Recording_ForASecondOpenAttempt_AfterTheFirstVerified_IsRefused()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var device = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());

            // Two attempts proven side by side — two phones, or a sheet abandoned mid-way — and then
            // both recordings arrive.
            var (first, _) = await ProveAsync(harness, storage, home, match, device);
            var (second, _) = await ProveAsync(harness, storage, home, match, device);

            await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .AttachEvidence(first.VerificationId, Clip(), new AttachVerificationEvidenceRequest());

            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .AttachEvidence(second.VerificationId, Clip(), new AttachVerificationEvidenceRequest()),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("already verified"));

            storage.Verify(
                s => s.UploadVideoAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Once,
                "the refused clip is never stored");
            Assert.That(Verification(harness, second.VerificationId).Status, Is.EqualTo(MatchVerificationStatus.BiometricVerified));

            // The attempt that did complete still answers a retry with its record.
            var retried = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .AttachEvidence(first.VerificationId, Clip(), new AttachVerificationEvidenceRequest());
            Assert.That(retried.Status, Is.EqualTo(MatchVerificationStatus.Verified));
        }

        [TestCase("ios", "vendor-id-1")]
        [TestCase("android", "android-id-1")]
        public async Task OsAndAppUpdate_IsTheSamePhone_AndRaisesNoFlag(string platform, string platformId)
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var request = Phone();
            request.Platform = platform;
            request.PlatformDeviceId = platformId;
            var device = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(request);
            // A phone this account has had for a month, with the key it was given then.
            await SetDeviceRegisteredOn(harness, device.DeviceId, DateTime.UtcNow.AddDays(-30));

            // The OS and the app have both updated since. Neither touches the installation id, its key
            // or the platform id, so the phone only reports a newer version — no registration, no key.
            var start = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .Start(match, new StartMatchVerificationRequest { DeviceId = device.DeviceId, OsVersion = "19.0", AppVersion = "2.20.0" });
            await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .SubmitBiometricProof(start.VerificationId, new SubmitBiometricProofRequest
                {
                    Signature = VerificationProof.Sign(device.Secret, start.Message),
                });
            await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest());

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object, role: "Admin")
                .GetPanel(match);
            var record = panel.Records.Single(r => r.UserId == home);

            Assert.That(record.Device!.OsVersion, Is.EqualTo("19.0"));
            Assert.That(record.Device.AccountDeviceCount, Is.EqualTo(1));
            Assert.That(record.Flags, Is.Empty, "an update is not a new phone, and it issues no new key");

            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<UserDeviceEntity>().Count(d => d.UserId == home), Is.EqualTo(1), "still one device row");
        }

        [Test]
        public async Task Panel_NamesTheAccountThatWasOnThePhoneFirst()
        {
            // The case this is for: Žika's phone, with Žika's account on it for weeks — then Pera's
            // account signs in on it and verifies Pera's match. "Shared phone" says little; the name
            // of the account that was there first says who may have played.
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var zika = Guid.NewGuid();
            SeedUsers(harness, zika);

            var zikasPhone = await harness.NewMatchVerificationServiceAsUser(zika, storage.Object).RegisterDevice(Phone());
            await SetDeviceRegisteredOn(harness, zikasPhone.DeviceId, DateTime.UtcNow.AddDays(-21));

            var perasRegistration = await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .RegisterDevice(Phone(zikasPhone.DeviceId));
            await VerifyAsync(harness, storage, home, match, perasRegistration);

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object, role: "Admin")
                .GetPanel(match);
            var record = panel.Records.Single(r => r.UserId == home);

            var other = record.Device!.OtherAccounts.Single();
            Assert.That(other.UserId, Is.EqualTo(zika));
            Assert.That(other.Username, Is.EqualTo($"player-{zika.ToString()[..4]}"));
            Assert.That(other.CameFirst, Is.True);
            Assert.That(other.FirstSeenOn, Is.LessThan(DateTime.UtcNow.AddDays(-20)));
            Assert.That(record.Device.OtherAccountsOnDevice, Is.EqualTo(1));
            Assert.That(record.Flags, Does.Contain("sharedDevice"));

            // The player's own view of the same record never names anyone.
            var own = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).GetPanel(match);
            Assert.That(own.Mine!.Device, Is.Not.Null, "their own phone is theirs to see");
            Assert.That(own.Mine.Device!.OtherAccounts, Is.Empty);
            Assert.That(own.Mine.Device.OtherAccountsOnDevice, Is.EqualTo(0));
        }

        [Test]
        public async Task Panel_AnAccountThatJoinedThePhoneLater_IsNamed_ButNotAsFirst()
        {
            // The ordinary counterpart: Pera's own phone, and a brother who later plays on it too.
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var brother = Guid.NewGuid();
            SeedUsers(harness, brother);

            var perasPhone = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(Phone());
            await SetDeviceRegisteredOn(harness, perasPhone.DeviceId, DateTime.UtcNow.AddDays(-21));
            await harness.NewMatchVerificationServiceAsUser(brother, storage.Object).RegisterDevice(Phone(perasPhone.DeviceId));
            await VerifyAsync(harness, storage, home, match, perasPhone);

            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object, role: "Admin")
                .GetPanel(match);
            var other = panel.Records.Single(r => r.UserId == home).Device!.OtherAccounts.Single();

            Assert.That(other.UserId, Is.EqualTo(brother));
            Assert.That(other.CameFirst, Is.False);
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────

        private static async Task SetDeviceRegisteredOn(BracketTestHarness harness, Guid deviceId, DateTime registeredOn)
        {
            using var ctx = harness.ReadContext();
            var row = ctx.Set<UserDeviceEntity>().Single(d => d.DeviceId == deviceId);
            row.CreatedOn = registeredOn;
            row.KeyIssuedOn = registeredOn;
            await ctx.SaveChangesAsync();
        }

        private static async Task<(BracketTestHarness Harness, Guid TournamentId, Guid MatchId, Guid HomeUserId)> SetUpAsync(
            bool requireVerification = true,
            bool requireResultApproval = false)
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, 4, requireResultApproval: requireResultApproval);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            using (var ctx = harness.ReadContext())
            {
                ctx.Set<TournamentEntity>().Single(t => t.Id == tid).RequireResultVerification = requireVerification;
                await ctx.SaveChangesAsync();
            }

            var match = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var home = harness.ParticipantUserId(match.HomeParticipantId!.Value);
            var away = harness.ParticipantUserId(match.AwayParticipantId!.Value);

            // The panel reads the players' names; the harness seeds participants without user rows.
            SeedUsers(harness, home, away);

            await harness.DenyManageFor(home, tid);
            return (harness, tid, match.Id!.Value, home);
        }

        private static void SeedUsers(BracketTestHarness harness, params Guid[] userIds)
        {
            using var ctx = harness.ReadContext();
            foreach (var id in userIds)
            {
                if (ctx.Set<UserEntity>().IgnoreQueryFilters().Any(u => u.Id == id)) continue;
                ctx.Add(new UserEntity { Id = id, Username = $"player-{id.ToString()[..4]}", IsDeleted = false });
            }
            ctx.SaveChanges();
        }

        private static async Task<(StartMatchVerificationResponse Start, RegisterVerificationDeviceResponse Device)> ProveAsync(
            BracketTestHarness harness,
            Mock<IStorageService> storage,
            Guid userId,
            Guid matchId,
            RegisterVerificationDeviceResponse? device = null)
        {
            device ??= await harness.NewMatchVerificationServiceAsUser(userId, storage.Object).RegisterDevice(Phone());

            var start = await harness.NewMatchVerificationServiceAsUser(userId, storage.Object)
                .Start(matchId, new StartMatchVerificationRequest { DeviceId = device.DeviceId });

            // What the phone does after Face ID releases the key: sign the exact message it was handed.
            await harness.NewMatchVerificationServiceAsUser(userId, storage.Object)
                .SubmitBiometricProof(start.VerificationId, new SubmitBiometricProofRequest
                {
                    Signature = VerificationProof.Sign(device.Secret, start.Message),
                });

            return (start, device);
        }

        private static async Task<MatchVerificationRecordDto> VerifyAsync(
            BracketTestHarness harness,
            Mock<IStorageService> storage,
            Guid userId,
            Guid matchId,
            RegisterVerificationDeviceResponse? device = null)
        {
            var (start, _) = await ProveAsync(harness, storage, userId, matchId, device);

            return await harness.NewMatchVerificationServiceAsUser(userId, storage.Object)
                .AttachEvidence(start.VerificationId, Clip(), new AttachVerificationEvidenceRequest
                {
                    DurationMs = 32000,
                    RecordedOn = DateTime.UtcNow.AddMinutes(-2),
                    FileName = "RPReplay_Final.MP4",
                });
        }

        private static RegisterVerificationDeviceRequest Phone(Guid? deviceId = null) => new()
        {
            DeviceId = deviceId,
            Platform = "ios",
            DeviceModel = "iPhone 15 Pro",
            DeviceBrand = "Apple",
            OsVersion = "18.1",
            AppVersion = "2.19.0",
            IsPhysicalDevice = true,
            PlatformDeviceId = "vendor-id-1",
        };

        private static IFormFile Clip(string contentType = "video/mp4")
        {
            var bytes = new byte[] { 0, 0, 0, 24, 102, 116, 121, 112 };
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "clip.mp4")
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType,
            };
        }

        private static Mock<IStorageService> Storage()
        {
            var storage = new Mock<IStorageService>();
            storage
                .Setup(s => s.UploadVideoAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(() => new StoredAsset($"https://cdn.test/{Guid.NewGuid()}.mp4", Guid.NewGuid().ToString(), StorageProviderType.Cloudinary));
            return storage;
        }

        private static MatchResultDto Score(Guid tournamentId, Guid matchId) => new()
        {
            MatchId = matchId,
            TournamentId = tournamentId,
            HomeScore = 2,
            AwayScore = 0,
        };

        private static MatchResultVerificationEntity Verification(BracketTestHarness harness, Guid id)
        {
            using var ctx = harness.ReadContext();
            return ctx.Set<MatchResultVerificationEntity>().AsNoTracking().Single(v => v.Id == id);
        }
    }
}
