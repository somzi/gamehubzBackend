using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Services;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Bracket
{
    internal sealed partial class ResultVerificationTests
    {
        private static async Task<TournamentPhoneBindingDto> BindPhone(BracketTestHarness harness, Guid user, Guid tournament, Guid deviceId) =>
            await harness.NewVerificationPhoneServiceAsUser(user).Bind(tournament, new() { DeviceId = deviceId, Platform = "android" });

        private static RegisterVerificationDeviceRequest DistinctPhone(string hash) => new()
            { Platform = "android", PlatformDeviceId = hash, DeviceModel = hash, IsPhysicalDevice = true };

        private static List<(Guid Id, DateTime? ModifiedOn, string KeySecret)> PhoneRows(BracketTestHarness harness, Guid user)
        {
            using var ctx = harness.ReadContext();
            return ctx.Set<UserDeviceEntity>().AsNoTracking().Where(d => d.UserId == user)
                .Select(d => new { d.Id, d.ModifiedOn, d.KeySecret }).AsEnumerable()
                .Select(d => (d.Id!.Value, d.ModifiedOn, d.KeySecret)).OrderBy(d => d.Item1).ToList();
        }

        [Test]
        public async Task PhoneBinding_AndDecision_NeverRewriteThePhonesOwnRow()
        {
            // A binding only points at a phone. Writing the phone row back from the copy loaded for the
            // check would put an old key back over one the phone re-issued meanwhile.
            var (harness, tid, _, home) = await SetUpAsync();
            var service = harness.NewMatchVerificationServiceAsUser(home, Storage().Object);
            var first = await service.RegisterDevice(DistinctPhone("first"));
            var second = await service.RegisterDevice(DistinctPhone("second"));
            var before = PhoneRows(harness, home);

            await BindPhone(harness, home, tid, first.DeviceId);
            Assert.That((await BindPhone(harness, home, tid, second.DeviceId)).Status, Is.EqualTo(TournamentPlayerDeviceStatus.Pending));
            var manager = harness.NewVerificationPhoneServiceAsUser(BracketTestHarness.OwnerUserId, "Admin");
            var pending = (await manager.GetPending(tid)).Single();
            await harness.NewVerificationPhoneServiceAsUser(BracketTestHarness.OwnerUserId, "Admin")
                .Decide(tid, pending.Id, new() { UserDeviceId = pending.RequestedPhone.UserDeviceId }, approve: true);

            Assert.That(PhoneRows(harness, home), Is.EqualTo(before));
        }

        [Test]
        public async Task PendingPhone_ShowsOnlyOnThatPhone_InThePlayersOwnMatch()
        {
            var (harness, tid, match, home) = await SetUpAsync();
            var storage = Storage();
            var service = harness.NewMatchVerificationServiceAsUser(home, storage.Object);
            var bound = await service.RegisterDevice(DistinctPhone("bound"));
            var other = await service.RegisterDevice(DistinctPhone("other"));
            await BindPhone(harness, home, tid, bound.DeviceId);
            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .Start(match, new() { DeviceId = other.DeviceId, SeriesNumber = 1, GameNumber = 1 }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("organizer approval"));

            Assert.That((await service.GetPanel(match, other.DeviceId)).PhoneApprovalPending, Is.True);
            Assert.That((await service.GetPanel(match, bound.DeviceId)).PhoneApprovalPending, Is.False, "the bound phone verifies as usual");

            var someoneElses = harness.Matches(tid).First(m => m.RoundNumber == 1 && m.Id != match).Id!.Value;
            Assert.That((await service.GetPanel(someoneElses, other.DeviceId)).PhoneApprovalPending, Is.False,
                "a match the player does not play says nothing about their phone");
        }

        [Test]
        public void PhoneIdentity_NullHashesDoNotMatch_ButReinstalledPlatformIdentityDoes()
        {
            var first = new UserDeviceEntity { Id = Guid.NewGuid() };
            var second = new UserDeviceEntity { Id = Guid.NewGuid() };
            Assert.That(TournamentVerificationPhoneService.SamePhone(first, second), Is.False);
            Assert.That(TournamentVerificationPhoneService.SamePhone(first, first), Is.True);
            first.PlatformDeviceIdHash = second.PlatformDeviceIdHash = "same-physical-phone";
            Assert.That(TournamentVerificationPhoneService.SamePhone(first, second), Is.True);
            second.PlatformDeviceIdHash = "other";
            Assert.That(TournamentVerificationPhoneService.SamePhone(first, second), Is.False);
        }

        [Test]
        public async Task PhoneEnrollment_OnlyPreviouslyVerifiedPhonesRequireReview()
        {
            var (harness, tid, _, home) = await SetUpAsync();
            var service = harness.NewMatchVerificationServiceAsUser(home, Storage().Object);
            await service.RegisterDevice(DistinctPhone("never-verified"));
            var phone = await service.RegisterDevice(DistinctPhone("first-playing-phone"));
            var result = await BindPhone(harness, home, tid, phone.DeviceId);
            Assert.That(result.Status, Is.EqualTo(TournamentPlayerDeviceStatus.Active));
            var retry = await BindPhone(harness, home, tid, phone.DeviceId);
            Assert.That(retry.Status, Is.EqualTo(TournamentPlayerDeviceStatus.Active));
            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<TournamentPlayerDeviceEntity>().Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task PhoneEnrollment_HistoryFromOtherTournaments_RequiresApprovalForANewPhone()
        {
            var (harness, tid, _, home) = await SetUpAsync();
            var old = await harness.NewMatchVerificationServiceAsUser(home, Storage().Object).RegisterDevice(DistinctPhone("old"));
            using (var ctx = harness.ReadContext())
            {
                ctx.Set<UserDeviceEntity>().Single(x => x.DeviceId == old.DeviceId).LastVerifiedOn = DateTime.UtcNow.AddDays(-3);
                await ctx.SaveChangesAsync();
            }
            var phone = await harness.NewMatchVerificationServiceAsUser(home, Storage().Object).RegisterDevice(DistinctPhone("new"));
            Assert.That((await BindPhone(harness, home, tid, phone.DeviceId)).Status,
                Is.EqualTo(TournamentPlayerDeviceStatus.Pending));
        }

        [Test]
        public async Task PhoneEnrollment_AndroidReinstall_KeepsTheApprovedPhone()
        {
            var (harness, tid, _, home) = await SetUpAsync();
            var service = harness.NewMatchVerificationServiceAsUser(home, Storage().Object);
            var first = await service.RegisterDevice(DistinctPhone("android-physical-id"));
            await BindPhone(harness, home, tid, first.DeviceId);
            var reinstalled = await service.RegisterDevice(DistinctPhone("android-physical-id"));
            Assert.That(reinstalled.DeviceId, Is.Not.EqualTo(first.DeviceId));
            Assert.That((await BindPhone(harness, home, tid, reinstalled.DeviceId)).Status,
                Is.EqualTo(TournamentPlayerDeviceStatus.Active));
            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<TournamentPlayerDeviceEntity>().Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task PhoneChange_StopsBeforeAttempt_UpdatesPending_AndRejectsStaleOrganizerDecisions()
        {
            var (harness, tid, match, home) = await SetUpAsync();
            var storage = Storage();
            var service = harness.NewMatchVerificationServiceAsUser(home, storage.Object);
            var first = await service.RegisterDevice(DistinctPhone("first"));
            await BindPhone(harness, home, tid, first.DeviceId);
            var second = await service.RegisterDevice(DistinctPhone("second"));
            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object).Start(match,
                new() { DeviceId = second.DeviceId, SeriesNumber = 1, GameNumber = 1 }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("organizer approval"));
            using (var ctx = harness.ReadContext()) Assert.That(ctx.Set<MatchResultVerificationEntity>().Count(), Is.Zero);
            Assert.That((await service.GetPanel(match, second.DeviceId)).PhoneApprovalPending, Is.True);

            Assert.That((await service.GetPanel(match, first.DeviceId)).PhoneApprovalPending, Is.False);
            Assert.That((await service.GetPanel(match)).PhoneApprovalPending, Is.False);
            Assert.That((await service.GetPanel(match, Guid.NewGuid())).PhoneApprovalPending, Is.False);
            var reinstall = await service.RegisterDevice(DistinctPhone("first"));
            Assert.That((await service.GetPanel(match, reinstall.DeviceId)).PhoneApprovalPending, Is.False);
            await harness.NewMatchVerificationServiceAsUser(home, storage.Object).Start(match, new() { DeviceId = first.DeviceId, SeriesNumber = 1, GameNumber = 1 });

            var manager = BracketTestHarness.OwnerUserId;
            var pending = (await harness.NewVerificationPhoneServiceAsUser(manager, "Admin").GetPending(tid)).Single();
            await BindPhone(harness, home, tid, second.DeviceId);
            var third = await service.RegisterDevice(DistinctPhone("third"));
            await BindPhone(harness, home, tid, third.DeviceId);
            var latest = (await harness.NewVerificationPhoneServiceAsUser(manager, "Admin").GetPending(tid)).Single();
            Assert.That(latest.Id, Is.EqualTo(pending.Id));
            Assert.That(latest.RequestedPhone.UserDeviceId, Is.Not.EqualTo(pending.RequestedPhone.UserDeviceId));
            Assert.That(async () => await harness.NewVerificationPhoneServiceAsUser(manager, "Admin").Decide(tid, pending.Id,
                new() { UserDeviceId = pending.RequestedPhone.UserDeviceId }, true),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("changed"));
            await harness.NewVerificationPhoneServiceAsUser(manager, "Admin").Decide(tid, latest.Id,
                new() { UserDeviceId = latest.RequestedPhone.UserDeviceId }, true);
            using (var ctx = harness.ReadContext())
            {
                var rows = ctx.Set<TournamentPlayerDeviceEntity>().ToList();
                Assert.That(rows.Count(x => x.Status == TournamentPlayerDeviceStatus.Active), Is.EqualTo(1));
                Assert.That(rows.Count(x => x.Status == TournamentPlayerDeviceStatus.Replaced), Is.EqualTo(1));
                Assert.That(rows.Single(x => x.Status == TournamentPlayerDeviceStatus.Active).DecidedByUserId, Is.EqualTo(manager));
            }
            Assert.That((await harness.NewMatchVerificationServiceAsUser(home, storage.Object).GetPanel(match)).PhoneApprovalPending, Is.False);
            await harness.NewMatchVerificationServiceAsUser(home, storage.Object).Start(match,
                new() { DeviceId = third.DeviceId, SeriesNumber = 1, GameNumber = 1 });
            Assert.That((await BindPhone(harness, home, tid, first.DeviceId)).Status,
                Is.EqualTo(TournamentPlayerDeviceStatus.Pending));
        }

        [Test]
        public async Task RejectedPhone_NeedsANewPendingRequest_EvenWithNoActivePhone()
        {
            var (harness, tid, _, home) = await SetUpAsync();
            var phone = await harness.NewMatchVerificationServiceAsUser(home, Storage().Object).RegisterDevice(DistinctPhone("rejected"));
            using (var ctx = harness.ReadContext())
            {
                ctx.Add(new TournamentPlayerDeviceEntity { Id = Guid.NewGuid(), TournamentId = tid, UserId = home,
                    UserDeviceId = ctx.Set<UserDeviceEntity>().Single(x => x.DeviceId == phone.DeviceId).Id!.Value,
                    Status = TournamentPlayerDeviceStatus.Pending, RequestedOn = DateTime.UtcNow, IsDeleted = false });
                await ctx.SaveChangesAsync();
            }
            var manager = harness.NewVerificationPhoneServiceAsUser(BracketTestHarness.OwnerUserId, "Admin");
            var pending = (await manager.GetPending(tid)).Single();
            await manager.Decide(tid, pending.Id, new() { UserDeviceId = pending.RequestedPhone.UserDeviceId }, false);
            Assert.That((await BindPhone(harness, home, tid, phone.DeviceId)).Status,
                Is.EqualTo(TournamentPlayerDeviceStatus.Pending));
            using var read = harness.ReadContext();
            Assert.That(read.Set<TournamentPlayerDeviceEntity>().Count(x => x.Status == TournamentPlayerDeviceStatus.Rejected), Is.EqualTo(1));
            Assert.That(read.Set<TournamentPlayerDeviceEntity>().Count(x => x.Status == TournamentPlayerDeviceStatus.Pending), Is.EqualTo(1));
        }

        [Test]
        public async Task PhoneEndpoints_RequireParticipation_KnownPlatform_AndManagerRights()
        {
            var (harness, tid, _, home) = await SetUpAsync();
            var outsider = Guid.NewGuid();
            await harness.DenyManageFor(outsider, tid);
            var phone = await harness.NewMatchVerificationServiceAsUser(home, Storage().Object).RegisterDevice(Phone());
            Assert.That(async () => await BindPhone(harness, outsider, tid, phone.DeviceId),
                Throws.TypeOf<BusinessRuleException>());
            Assert.That(async () => await harness.NewVerificationPhoneServiceAsUser(home).Bind(tid, new() { Platform = "web" }),
                Throws.TypeOf<BusinessRuleException>());
            Assert.That(async () => await harness.NewVerificationPhoneServiceAsUser(home).GetPending(tid), Throws.TypeOf<BusinessRuleException>());
            Assert.That(async () => await harness.NewVerificationPhoneServiceAsUser(outsider).Decide(tid, Guid.NewGuid(), new(), true),
                Throws.TypeOf<BusinessRuleException>());
            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<TournamentPlayerDeviceEntity>().Any(), Is.False);
            Assert.That(ctx.Set<UserDeviceEntity>().Count(), Is.EqualTo(1), "a refused join records no phone");
        }

        [Test]
        public async Task Join_PhoneThatNeverVerified_IsRecordedWithoutAKey_AndKeepsItsBindingWhenTheKeyIsIssued()
        {
            var (harness, tid, match, home) = await SetUpAsync();
            var storage = Storage();
            var joined = await harness.NewVerificationPhoneServiceAsUser(home).Bind(tid, DistinctPhone("joined"));
            Assert.That(joined.Status, Is.EqualTo(TournamentPlayerDeviceStatus.Active));
            Assert.That(joined.DeviceId, Is.Not.EqualTo(Guid.Empty), "the phone receives its installation id at join");
            var keyless = PhoneRows(harness, home).Single();
            Assert.That(keyless.KeySecret, Is.Empty);

            // First verification: the phone registers with the id it got at join.
            var registration = DistinctPhone("joined");
            registration.DeviceId = joined.DeviceId;
            var issued = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(registration);
            Assert.That(issued.DeviceId, Is.EqualTo(joined.DeviceId));
            var row = PhoneRows(harness, home).Single();
            Assert.That(row.Id, Is.EqualTo(keyless.Id), "the key is issued on the row the binding points at");
            Assert.That(row.KeySecret, Is.EqualTo(issued.Secret));

            await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                .Start(match, new() { DeviceId = joined.DeviceId, SeriesNumber = 1, GameNumber = 1 });
            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<TournamentPlayerDeviceEntity>().Single().Status, Is.EqualTo(TournamentPlayerDeviceStatus.Active));
        }

        [Test]
        public async Task Join_RecordedPhone_MakesAnotherPhoneWaitForApproval_EvenWithoutVerificationHistory()
        {
            // The account has never verified anywhere, so without the phone recorded at join the first
            // phone to verify, whoever holds it, would simply become the tournament's phone.
            var (harness, tid, match, home) = await SetUpAsync();
            var storage = Storage();
            await harness.NewVerificationPhoneServiceAsUser(home).Bind(tid, DistinctPhone("owner"));
            var ringer = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).RegisterDevice(DistinctPhone("ringer"));
            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object)
                    .Start(match, new() { DeviceId = ringer.DeviceId, SeriesNumber = 1, GameNumber = 1 }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("organizer approval"));

            var pending = (await harness.NewVerificationPhoneServiceAsUser(BracketTestHarness.OwnerUserId, "Admin").GetPending(tid)).Single();
            Assert.That(pending.ActivePhone?.DeviceModel, Is.EqualTo("owner"));
            Assert.That(pending.RequestedPhone.DeviceModel, Is.EqualTo("ringer"));
        }

        [Test]
        public async Task Join_KeylessPhone_CannotStartOrSign_UntilItsKeyIsIssued()
        {
            var (harness, tid, match, home) = await SetUpAsync();
            var joined = await harness.NewVerificationPhoneServiceAsUser(home).Bind(tid, DistinctPhone("joined"));
            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, Storage().Object)
                    .Start(match, new() { DeviceId = joined.DeviceId, SeriesNumber = 1, GameNumber = 1 }),
                Throws.TypeOf<VerificationDeviceUnknownException>(), "409 sends the phone to register first");
            using (var ctx = harness.ReadContext()) Assert.That(ctx.Set<MatchResultVerificationEntity>().Count(), Is.Zero);

            // HMAC accepts an empty key, so an empty key must never count as a key.
            const string message = "gamehubz.verify.v1|attempt";
            Assert.That(VerificationProof.IsValid(string.Empty, message, VerificationProof.Sign(string.Empty, message)), Is.False);
        }

        [Test]
        public async Task Join_NamingAnotherAccountsInstallation_RecordsOnlyTheCallersOwnRow()
        {
            // A shared phone: one installation id, a row per account. The other account's row and key
            // are never touched, and the binding points at the caller's own row.
            var (harness, tid, _, home) = await SetUpAsync();
            var other = Guid.NewGuid();
            var theirs = await harness.NewMatchVerificationServiceAsUser(other, Storage().Object).RegisterDevice(DistinctPhone("shared"));
            var before = PhoneRows(harness, other);

            var joined = await BindPhone(harness, home, tid, theirs.DeviceId);
            Assert.That(joined.DeviceId, Is.EqualTo(theirs.DeviceId));
            Assert.That(PhoneRows(harness, other), Is.EqualTo(before));
            var own = PhoneRows(harness, home).Single();
            Assert.That(own.KeySecret, Is.Empty);
            using var ctx = harness.ReadContext();
            Assert.That(ctx.Set<TournamentPlayerDeviceEntity>().Single().UserDeviceId, Is.EqualTo(own.Id));
        }

        [Test]
        public async Task Join_NewPhoneRows_SpendTheDailyKeyBudget_ButAKnownPhoneDoesNot()
        {
            var (harness, tid, _, home) = await SetUpAsync();
            var service = harness.NewMatchVerificationServiceAsUser(home, Storage().Object);
            var first = await service.RegisterDevice(DistinctPhone("phone-0"));
            for (int i = 1; i < 10; i++) await service.RegisterDevice(DistinctPhone($"phone-{i}"));

            Assert.That(async () => await harness.NewVerificationPhoneServiceAsUser(home).Bind(tid, DistinctPhone("eleventh")),
                Throws.TypeOf<BusinessRuleException>());
            Assert.That(PhoneRows(harness, home), Has.Count.EqualTo(10));
            Assert.That((await BindPhone(harness, home, tid, first.DeviceId)).Status, Is.EqualTo(TournamentPlayerDeviceStatus.Active));
        }

        [TestCase(null, null)]
        [TestCase(1, null)]
        [TestCase(null, 1)]
        public async Task Start_RequiresBothGameNumbers(int? series, int? game)
        {
            var (harness, _, match, home) = await SetUpAsync();
            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, Storage().Object).Start(match,
                new() { DeviceId = Guid.NewGuid(), SeriesNumber = series, GameNumber = game }),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("Update GameHubz"));
        }

        [Test]
        public async Task PendingPhones_AppearInManagerInboxAndBadges_ThenDisappearAfterApproval()
        {
            var (harness, tid, _, home) = await SetUpAsync();
            var other = Guid.NewGuid();
            SeedUsers(harness, other, BracketTestHarness.OwnerUserId);
            var service = harness.NewMatchVerificationServiceAsUser(home, Storage().Object);
            var original = await service.RegisterDevice(DistinctPhone("original"));
            await BindPhone(harness, home, tid, original.DeviceId);
            await harness.NewMatchVerificationServiceAsUser(other, Storage().Object).RegisterDevice(DistinctPhone("shared-new-phone"));
            var next = await service.RegisterDevice(DistinctPhone("shared-new-phone"));
            await BindPhone(harness, home, tid, next.DeviceId);

            var pending = (await harness.NewVerificationPhoneServiceAsUser(BracketTestHarness.OwnerUserId).GetPending(tid)).Single();
            Assert.That(pending.UserId, Is.EqualTo(home));
            Assert.That(pending.ActivePhone!.DeviceModel, Is.EqualTo("original"));
            Assert.That(pending.RequestedPhone.DeviceModel, Is.EqualTo("shared-new-phone"));
            Assert.That(pending.OtherAccounts.Select(x => x.UserId), Is.EqualTo(new[] { other }));

            // Hub owners can lack a UserHub row. They must still see the request badge.
            var badges = await harness.NewBadgeServiceAsUser(BracketTestHarness.OwnerUserId).ComputeAsync(BracketTestHarness.OwnerUserId);
            var breakdown = await harness.NewBadgeServiceAsUser(BracketTestHarness.OwnerUserId).GetApprovalsBreakdownAsync(BracketTestHarness.OwnerUserId);
            Assert.That(badges.PendingVerificationPhones, Is.EqualTo(1));
            Assert.That(breakdown.Tournaments.Single(x => x.TournamentId == tid).VerificationPhones, Is.EqualTo(1));
            Assert.That(badges.HubManageTotal, Is.EqualTo(breakdown.Hubs.Sum(x => x.Count)));
            await harness.NewVerificationPhoneServiceAsUser(BracketTestHarness.OwnerUserId).Decide(tid, pending.Id,
                new() { UserDeviceId = pending.RequestedPhone.UserDeviceId }, true);
            Assert.That((await harness.NewVerificationPhoneServiceAsUser(BracketTestHarness.OwnerUserId).GetPending(tid)), Is.Empty);
            Assert.That((await harness.NewBadgeServiceAsUser(BracketTestHarness.OwnerUserId).ComputeAsync(BracketTestHarness.OwnerUserId)).PendingVerificationPhones, Is.Zero);
        }

        [TestCase(TournamentPlayerDeviceStatus.Active)]
        [TestCase(TournamentPlayerDeviceStatus.Pending)]
        public async Task Database_DoesNotAllowTwoActiveOrTwoPendingPhones(TournamentPlayerDeviceStatus status)
        {
            var (harness, tid, _, home) = await SetUpAsync();
            var phone = await harness.NewMatchVerificationServiceAsUser(home, Storage().Object).RegisterDevice(Phone());
            using var ctx = harness.ReadContext();
            Guid deviceId = ctx.Set<UserDeviceEntity>().Single(x => x.DeviceId == phone.DeviceId).Id!.Value;
            for (int i = 0; i < 2; i++) ctx.Add(new TournamentPlayerDeviceEntity
                { Id = Guid.NewGuid(), TournamentId = tid, UserId = home, UserDeviceId = deviceId, Status = status,
                    IsDeleted = false, RequestedOn = DateTime.UtcNow });
            Assert.That(async () => await ctx.SaveChangesAsync(), Throws.TypeOf<DbUpdateException>());
        }

        [TestCase("missing")]
        [TestCase("biometric-replay")]
        [TestCase("other-phone")]
        public async Task Upload_RequiresItsOwnDeviceSignature_BeforeAnyStorageOrClaim(string source)
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var (start, device) = await ProveAsync(harness, storage, home, match);
            var metadata = RecordingOf(1, 1);
            metadata.Signature = source == "missing" ? null : source == "biometric-replay"
                ? VerificationProof.Sign(device.Secret, start.Message)
                : VerificationProof.Sign(VerificationProof.NewSecret(), VerificationProof.BuildEvidenceMessage(start.VerificationId, match, home, device.DeviceId, start.Challenge));
            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object).AttachEvidence(start.VerificationId, Clip(), metadata),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains(source == "missing" ? "Update GameHubz" : "phone used"));
            using var ctx = harness.ReadContext();
            var attempt = ctx.Set<MatchResultVerificationEntity>().Single();
            Assert.That(attempt.Status, Is.EqualTo(MatchVerificationStatus.BiometricVerified));
            Assert.That(attempt.EvidenceUploadClaimedOn, Is.Null);
            Assert.That(ctx.Set<MatchEvidenceEntity>().Count(), Is.Zero);
            storage.Verify(x => x.UploadVideoAsync(It.IsAny<Microsoft.AspNetCore.Http.IFormFile>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task Upload_OutsideBiometricTime_IsRejected_ThenCorrectRecordingCanFinishTheSameAttempt()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var (start, _) = await ProveAsync(harness, storage, home, match);
            var wrong = SignedRecording(harness, start.VerificationId, new() { DurationMs = 30000, RecordedOn = DateTime.UtcNow.AddHours(-1) });
            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object).AttachEvidence(start.VerificationId, Clip(), wrong),
                Throws.TypeOf<VerificationRecordingRejectedException>().With.Message.Contains("wasn't made during this verification"));
            using (var ctx = harness.ReadContext())
            {
                Assert.That(ctx.Set<MatchEvidenceEntity>().Count(), Is.Zero);
                Assert.That(ctx.Set<MatchResultVerificationEntity>().Single().Status, Is.EqualTo(MatchVerificationStatus.BiometricVerified));
            }
            var retry = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).AttachEvidence(start.VerificationId, Clip(),
                SignedRecording(harness, start.VerificationId, RecordingOf(1, 1)));
            Assert.That(retry.Status, Is.EqualTo(MatchVerificationStatus.Verified));
        }

        [Test]
        public async Task BiometricResponse_IssuesEvidenceMessage_OnSuccessAndRetry_ButNotInPanels()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var (start, device) = await ProveAsync(harness, storage, home, match);
            var proof = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).SubmitBiometricProof(start.VerificationId,
                new() { Signature = VerificationProof.Sign(device.Secret, start.Message) });
            Assert.That(proof.EvidenceMessage, Does.StartWith("gamehubz.evidence.v1|"));
            Assert.That(proof.EvidenceMessage, Is.Not.EqualTo(start.Message));
            var verified = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).AttachEvidence(start.VerificationId, Clip(),
                new() { Signature = VerificationProof.Sign(device.Secret, proof.EvidenceMessage!) });
            Assert.That(verified.EvidenceMessage, Is.Null);
            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object, "Admin").GetPanel(match);
            Assert.That(panel.Games[0].Records.Single(x => x.UserId == home).Flags, Does.Contain("noRecordingTime"));
            Assert.That(panel.Games[0].Records.Single(x => x.UserId == home).EvidenceMessage, Is.Null);
        }

        [Test]
        public async Task Upload_NormalizesAndroidLocalTime_AndPreservesRawTimeForDuplicateDetection()
        {
            var (harness, tid, match, home) = await SetUpAsync();
            await SetSeriesFormat(harness, tid, match, 3, TeamWinCondition.MatchWins);
            var storage = Storage();
            var (first, device) = await ProveAsync(harness, storage, home, match);
            DateTime biometric;
            using (var ctx = harness.ReadContext()) biometric = ctx.Set<MatchResultVerificationEntity>().Single().BiometricVerifiedOn!.Value;
            var raw = biometric.AddHours(2).AddMinutes(-5);
            var metadata = new AttachVerificationEvidenceRequest { DurationMs = 30000, RecordedOn = raw, ClockOffsetMs = 300000, TimeZoneOffsetMinutes = 120 };
            var record = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).AttachEvidence(first.VerificationId, Clip(), SignedRecording(harness, first.VerificationId, metadata));
            Assert.That(record.RecordedOn, Is.EqualTo(biometric));
            using (var ctx = harness.ReadContext()) Assert.That(ctx.Set<MatchResultVerificationEntity>().Single().RawRecordedOn, Is.EqualTo(raw));
            var (second, _) = await ProveAsync(harness, storage, home, match, device, gameNumber: 2);
            // A slightly different measured clock offset must not disguise the same clip.
            metadata.ClockOffsetMs = 299900;
            Assert.That(async () => await harness.NewMatchVerificationServiceAsUser(home, storage.Object).AttachEvidence(second.VerificationId, Clip(), SignedRecording(harness, second.VerificationId, metadata)),
                Throws.TypeOf<VerificationRecordingRejectedException>().With.Message.Contains("already"));
        }

        [Test]
        public async Task Upload_UnusableRecordingTime_DoesNotDisplayExtremeMetadata()
        {
            var (harness, _, match, home) = await SetUpAsync();
            var storage = Storage();
            var (start, _) = await ProveAsync(harness, storage, home, match);
            var record = await harness.NewMatchVerificationServiceAsUser(home, storage.Object).AttachEvidence(start.VerificationId, Clip(),
                SignedRecording(harness, start.VerificationId, new() { RecordedOn = DateTime.MaxValue }));
            Assert.That(record.RecordedOn, Is.Null);
            var panel = await harness.NewMatchVerificationServiceAsUser(BracketTestHarness.OwnerUserId, storage.Object).GetPanel(match);
            Assert.That(panel.Games[0].Records.Single(x => x.UserId == home).Flags, Does.Contain("noRecordingTime"));
        }

        [TestCase(-90000, 0, 0, true)]
        [TestCase(90000, 0, 0, true)]
        [TestCase(-90001, 0, 0, false)]
        [TestCase(-600000, 600000, 0, true)]
        [TestCase(-1200000, 1200000, 0, false)]
        [TestCase(7200000, 0, 120, true)]
        [TestCase(-18000000, 0, -300, true)]
        [TestCase(7200000, 0, 9999, false)]
        public void RecordingTime_HandlesStartEndClockAndLocalTime(int delta, long clockOffset, int zone, bool expected)
        {
            var now = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
            Assert.That(VerificationRecordingTime.Overlaps(now.AddMilliseconds(delta), 30000, clockOffset, zone, now), Is.EqualTo(expected));
        }

        [Test]
        public void RecordingTime_MissingMetadataIsFlagged_AndExtremeDatesCannotOverflow()
        {
            Assert.That(VerificationRecordingTime.Overlaps(null, 30000, 0, 0, DateTime.UtcNow), Is.Null);
            Assert.That(VerificationRecordingTime.Overlaps(DateTime.UtcNow, null, 0, 0, DateTime.UtcNow), Is.Null);
            Assert.That(VerificationRecordingTime.Overlaps(DateTime.MaxValue, 30000, 900000, -840, DateTime.UtcNow), Is.False);
        }
    }
}
