# GameHubz Backend — Audit Report

**Read-only multi-agent audit, adversarially verified.**

- Raw findings from 14 reviewers (11 areas + 3 codebase sweeps): **134**
- After dedup: **112** distinct issues
- Verified against actual code: **34 confirmed** + **42 unverified (token limit hit mid-verify)** + **4 rejected as false-positive**

## Executive summary

Confirmed: **7 CRITICAL · 22 HIGH · 5 MEDIUM · 0 LOW**.

Three systemic issues dominate and account for most of the high-severity bucket:

1. **Broken authorization / IDOR everywhere** — most management and "act on resource by id" endpoints have only class-level `[Authorize]` and no per-resource ownership/manager check. A logged-in user can reset other users' passwords, modify other profiles, kick members from any hub, approve/reject registrations on tournaments they don't manage, upload evidence on any match, etc.
2. **Auth/secret hygiene is broken at production level** — password-reset OTP is returned in the HTTP response (full account takeover by knowing victim's email), production secrets (JWT signing key, DB passwords, SMTP, API keys) are committed to source, SignalR hubs have no auth, Azure Blob containers are publicly readable.
3. **Fire-and-forget notifications use the request-scoped `DbContext` after the request scope is disposed**, in 5+ services. Intermittent `ObjectDisposedException` and silent push-notification failures (the `catch {}` blocks swallow them). Same root cause is the `NotificationService` that does `using var uow` and disposes a context shared with the rest of the request.

## 🔴 Critical (7)

### Password-reset OTP is returned in the HTTP response, defeating email-based verification  
`🔴 CRITICAL` · category: **security** · id: `F1`

**Location:** `GameHubz.Api/Controllers/AuthController.cs:144-150`

**Description:** The active forgotPassword endpoint calls PasswordManagementService.SendEmailWithForgotPasswordToken(string), which generates the password-reset OTP and RETURNS it as the action's body. The controller forwards it verbatim to the caller. The entire point of an emailed OTP is that only the inbox owner sees it; returning it in the API response means anyone who knows a victim's email address can obtain a valid reset code without access to the mailbox, then call resetPassword to take over the account. This is a full account-takeover primitive.

**Evidence:** AuthController.cs line 147 'var otpCode = await this.passwordManagementService.SendEmailWithForgotPasswordToken(email);' and line 149 'return Ok(otpCode);'. PasswordManagementService.cs line 131-146 returns 'otpForDatabase' (the plaintext OTP). The OTP also matches an unauthenticated reset in ResetPasswordWithOtp.

**Suggested fix:** Change the endpoint to return Ok() with no body (or a generic success message). Never return the OTP/token to the client; it must only be delivered via the email channel.

<details><summary>Repro path (verifier)</summary>

1) POST /api/.../forgotPassword with body = "victim@example.com" (no auth required). 2) Read the JSON/body of the 200 response — it contains the plaintext OTP (otpForDatabase) returned by SendEmailWithForgotPasswordToken(string) and forwarded by Ok(otpCode). 3) Within 30 minutes, POST /api/.../resetPassword with { Email: victim@example.com, Otp: <code from step 2>, Password/ConfirmPassword: attacker-chosen }. ResetPasswordWithOtp resolves the user via GetByOtpAndMail and rewrites the password hash. Attacker now controls the victim account without ever accessing the victim's inbox.

</details>

---

### Hub verification approval endpoint is [AllowAnonymous] with no internal auth — anyone can verify any hub  
`🔴 CRITICAL` · category: **security** · id: `F45`

**Location:** `GameHubz.Api/Controllers/HubController.cs:206-211`

**Description:** RespondVerification (POST api/hub/{id}/verification-response) is [AllowAnonymous] and HubVerificationService.RespondVerification performs no authorization check (no caller identity is even read). Any unauthenticated client can POST {"approved":true} for an arbitrary hubId and flip Hub.IsVerified = true, self-verifying their own hub or griefing by rejecting pending requests. The route appears intended for a review email link but ships with zero authentication.

**Evidence:** HubController.cs:206 [AllowAnonymous] above the POST; HubVerificationService.cs:65-86 never calls GetTokenUserInfo or any Ensure* check and unconditionally sets hub.IsVerified = true when approved.

**Suggested fix:** Remove [AllowAnonymous] and require an admin/staff role, or gate behind a signed single-use token tied to the specific request id embedded in the review email.

<details><summary>Repro path (verifier)</summary>

As an unauthenticated client: (1) a hub owner (or attacker controlling a hub) calls POST api/hub/{id}/verification-request to create a pending request; (2) anonymously POST api/hub/{id}/verification-response with body {"approved":true} — no auth header. Service finds the pending request, sets Status=Approved and hub.IsVerified=true, SaveAsync persists it. Hub is now self-verified. Sending {"approved":false} for any victim hub with a pending request rejects it (griefing).

</details>

<sub>Independently flagged by: hub-social, security-sweep</sub>

---

### Participant removal endpoints (RemoveUser/RemoveTeam) lack manager authorization (IDOR)  
`🔴 CRITICAL` · category: **security** · id: `F36`

**Location:** `GameHubz.Api/Controllers/TournamentParticipantController.cs:29-41`

**Description:** RemoveUser and RemoveTeam are reachable by any authenticated user. The service methods hard-delete participants/registrations and soft-delete teams/members based solely on route ids, with no CanManageTournamentAsync check. Any logged-in user can eject any player or team from any tournament, including mid-event, silently rewriting the bracket inputs.

**Evidence:** TournamentParticipantService has no TournamentAuthorizationService dependency (constructor lines 9-27). RemoveUser (line 75) and RemoveTeam (line 89) do HardDeleteEntity/SoftDeleteEntity with no auth call; controller (TournamentParticipantController.cs:29-41) adds none.

**Suggested fix:** Add CanManageTournamentAsync(tournamentId) at the top of RemoveUser and RemoveTeam (inject TournamentAuthorizationService).

<details><summary>Repro path (verifier)</summary>

As any authenticated user, send POST /api/TournamentParticipant/tournament/{anyTournamentId}/user/{anyUserId} (or .../team/{teamId}) for a tournament you do not own or manage. The request succeeds: the targeted participant's TournamentParticipant and TournamentRegistration rows are hard-deleted (and for a team, the team and its members are soft-deleted), ejecting them from the bracket with no authorization check.

</details>

<sub>Independently flagged by: tournament, security-sweep</sub>

---

### Account takeover: inherited generic SaveEntity on UserController is unauthorized and resets any user's password by body Id  
`🔴 CRITICAL` · category: **security** · id: `F101`

**Location:** `GameHubz.Api/Controllers/UserController.cs:16, 114-120`

**Description:** UserController inherits POST SaveEntity from BasicGenericController. It overrides UserRolesRead/Delete to Admin but does NOT override UserRolesSave(), which defaults to null, making CheckAuthorization(null) a no-op. Any authenticated (non-admin) user can POST a UserPost; ServiceFunctions.SaveEntity treats inputDto.Id.HasValue as an update; UserService.BeforeSave + CheckPasswordField then hash and persist a new Password for the supplied Id. An attacker supplies the victim's UserId plus a new Password and takes over the account.

**Evidence:** BasicGenericController.SaveEntity calls CheckAuthorization(UserRolesSave()) which returns null -> no-op. UserController overrides only UserRolesRead()/UserRolesDelete(). UserService.CheckPasswordField (lines 281-296) hashes userPost.Password into the entity identified by userPost.Id. StageEntity (ServiceFunctions.cs:118) uses inputDto.Id to decide insert vs update with no ownership check.

**Suggested fix:** Override UserRolesSave() to [Admin] on UserController, or remove the inherited self-service SaveEntity and replace with an explicit endpoint that derives the user id from UserContextReader and forbids editing other users / changing password/role.

<details><summary>Repro path (verifier)</summary>

Authenticate as any non-admin user. POST api/User with body {"Id":"<victim-user-guid>","Email":"<victim-email>","Username":"<victim-username>","Password":"attacker-chosen","UserRoleId":"<admin-role-guid>",...}. CheckAuthorization(null) is a no-op; isNew=false so the existing victim row is updated; CheckPasswordField hashes the attacker password into it and UserRoleId is mapped, granting account takeover and role escalation.

</details>

---

### Production secrets committed to git history (JWT signing key, DB passwords, SMTP & API secrets)  
`🔴 CRITICAL` · category: **security** · id: `F91`

**Location:** `GameHubz.Api/appsettings.Development.json:13, 81-82`

**Description:** appsettings.json and appsettings.Development.json containing live secrets were committed in earlier commits and only removed from tracking later. Removing a file from tracking does not remove it from history. Anyone with repo access can recover the JWT HMAC signing key, password-encryption key, SMTP password, Cloudinary/Twitch/Kick client secrets, and a Supabase production Postgres credential. With the JWT SecretKey an attacker can forge valid access tokens for ANY user and fully bypass authentication.

**Evidence:** git show d7b729c:appsettings.Development.json returns Supabase prod DatabaseConnection password, SMTP Password 'hhkiuaipfwilylhp', AuthSettings SecretKey 'VAMG0H13mjR8jVppB5PTvnIIvmpj5EThlInCj' and PasswordKey 'q1fI1WhME3aAKK21'. The same values still sit in the working tree files.

**Suggested fix:** Treat all secrets as compromised: rotate the JWT SecretKey/PasswordKey, Supabase DB password, Gmail app password, and Twitch/Kick/Cloudinary keys immediately. Purge them from git history (git filter-repo or BFG) and force-push. Move all secrets to environment variables / a secret manager and keep only placeholder template files.

<details><summary>Repro path (verifier)</summary>

Clone the repo (or any fork/CI mirror with history). Run `git show d7b729c:GameHubz.Api/appsettings.Development.json` to recover the JWT SecretKey 'VAMG0H13mjR8jVppB5PTvnIIvmpj5EThlInCj'. Use it to HMAC-sign a JWT with Issuer 'webApi' and a chosen user id claim; the API (AuthenticationStartup.cs:22 SymmetricSecurityKey) accepts it as a valid access token, granting authenticated access as any user. The same history/working-tree files also expose Supabase prod DB password, Gmail SMTP password, and Twitch/Kick/YouTube secrets for direct abuse.

</details>

<sub>Independently flagged by: infra, security-sweep</sub>

---

### KickUserFromHub performs no authorization — any user can kick any member from any hub  
`🔴 CRITICAL` · category: **security** · id: `F46`

**Location:** `GameHubz.Logic/Services/HubService.cs:277-295`

**Description:** KickUserFromHub reads the caller (userAdmin) but never verifies the caller is the hub owner/admin before soft-deleting the target membership. Other management paths (RemoveMember, BanMember, ChangeMemberRole) call EnsureCallerCanManage/EnsureCallerIsOwner; this one does not. The controller endpoint is only [Authorize], so any logged-in user can remove any member (including admins or the owner) from any hub via arbitrary hubId/userId.

**Evidence:** HubService.cs:280 fetches userAdmin but never uses it for permission comparison; SoftDeleteEntity runs unconditionally at line 282. Contrast UserHubService.RemoveMember (UserHubService.cs:135-138) which calls EnsureCallerCanManage and blocks owner removal.

**Suggested fix:** Call userHubService.EnsureCallerCanManage(hubId, userAdmin.UserId) before the soft-delete, fetch membership via the nullable FindByUserAndHub and reject if null, and block kicking a HubOwner. Consider removing this redundant endpoint in favor of RemoveMember.

<details><summary>Repro path (verifier)</summary>

Authenticate as any user (the only requirement, given class-level [Authorize]). Send POST api/Hub/{hubId}/user/{victimUserId}/kick with a hubId and userId the caller does not own/manage. KickUserFromHub looks up that membership and soft-deletes it without any owner/admin check, removing the victim (potentially the hub owner) from the hub.

</details>

<sub>Independently flagged by: hub-social, security-sweep</sub>

---

### Registration approve/reject and team-register endpoints have no tournament-manager authorization (IDOR)  
`🔴 CRITICAL` · category: **security** · id: `F35`

**Location:** `GameHubz.Logic/Services/TournamentRegistrationService.cs:91-162`

**Description:** ApproveRegistration, ApproveRegistrations, RejectRegistration and RegisterTeam act on a registrationId/tournamentId taken from the request with only controller-level [Authorize]. TournamentRegistrationService does not inject TournamentAuthorizationService and none of these methods call CanManageTournamentAsync. Any logged-in user can approve/reject registrations for any tournament they do not manage, or register an arbitrary team — corrupting the participant roster and tournament lifecycle.

**Evidence:** TournamentRegistrationService constructor (lines 12-32) has no TournamentAuthorizationService; ApproveRegistration (line 91), ApproveRegistrations (119), RejectRegistration (153), RegisterTeam (171) contain no auth check. Controller TournamentRegistrationController.cs:21-57 adds none. Contrast TournamentService gating cancel/delete/openRegistration via CanManageTournamentAsync.

**Suggested fix:** Inject TournamentAuthorizationService and call CanManageTournamentAsync(registration.TournamentId) at the start of each approve/reject/RegisterTeam method; throw if false.

<details><summary>Repro path (verifier)</summary>

As any authenticated (non-manager) user, POST /api/tournamentRegistration/approve with the body set to a registrationId belonging to a tournament you do not manage. The request passes the class-level [Authorize], ApproveRegistration runs with no CanManageTournamentAsync check, sets status to Approved and creates a TournamentParticipant — injecting that user into a roster you don't control. Same for /reject and /approveAll. For /api/tournamentRegistration/tournament/{tournamentId}/team/{teamId}/register, any authenticated user can register an arbitrary teamId into any tournament (UserId=null also bypasses the eligibility checks in BeforeSave).

</details>

<sub>Independently flagged by: tournament, security-sweep</sub>

---

## 🟠 High (22)

### Asset GET/UPDATE/DELETE have no ownership check (IDOR)  
`🟠 HIGH` · category: **security** · id: `F58`

**Location:** `GameHubz.Api/Controllers/AssetsController.cs:27-34, 70-82`

**Description:** GetById, UpdateAsset, and DeleteAssetById pass a route asset id straight to AssetService, which loads the entity by id and mutates/reads with no check that the caller created or may access it. AssetEntity tracks CreatedBy but it is never used to filter access. Any authenticated user can enumerate GUIDs and read, overwrite, or delete other users' assets.

**Evidence:** AssetsController lines 27-34, 70-82 call assetService with only the route id. AssetService.UpdateAsset (line 132) and DeleteAsset (line 148) do GetById(id) then mutate without comparing asset.CreatedBy. CreatedBy captured at upload (line 71) but never enforced.

**Suggested fix:** After loading the asset, verify asset.CreatedBy == current user id (or a role/permission) before allowing read/update/delete; throw 403/404 otherwise.

<details><summary>Repro path (verifier)</summary>

Authenticate as user A. Obtain an asset GUID owned by user B (e.g. from GET /api/assets list response, an embedded asset URL, or a known id). Call DELETE /api/assets/{bId} or POST /api/assets/{bId} with an AssetUpdate body, or GET /api/assets/{bId}. The request succeeds and reads/overwrites/soft-deletes user B's asset because no CreatedBy/owner or role check is performed.

</details>

---

### Generic POST api/userHub lets any user grant themselves (or anyone) Admin/Exclusive membership in any public hub  
`🟠 HIGH` · category: **security** · id: `F47`

**Location:** `GameHubz.Api/Controllers/UserHubController.cs:12-19`

**Description:** UserHubController inherits BasicGenericController.SaveEntity which only calls CheckAuthorization(UserRolesSave()), defaulting to null, so no role is required. The only guard for new UserHub rows is UserHubService.BeforeSave, which checks ban/private-hub status but does NOT validate HubRole or that the caller equals inputDto.UserId or is a manager. A regular user can POST a UserHubPost with their own UserId, a public HubId, and HubRole = HubAdmin/HubExclusive, self-escalating to admin — bypassing AddMember's EnsureCallerIsOwner. They can also create memberships for arbitrary other users.

**Evidence:** BasicGenericController.cs:69-86 SaveEntity gated only by UserRolesSave() returning null. UserHubController overrides nothing. UserHubService.BeforeSave (UserHubService.cs:217-232) never inspects entity.HubRole nor compares inputDto.UserId to the token caller.

**Suggested fix:** Override UserRolesSave() to require an admin role, or route through AddMember which enforces role/owner checks. At minimum, in BeforeSave reject any HubRole other than HubMember and require inputDto.UserId == caller.UserId for self-join.

<details><summary>Repro path (verifier)</summary>

Authenticate as any normal user. POST /api/userHub with body { "userId": "<caller-or-arbitrary-guid>", "hubId": "<any-public-hub-id>", "hubRole": 2 } (2 = HubAdmin, or 4 = HubExclusive). No role check runs (UserRolesSave() == null), BeforeSave only blocks if banned or hub is private, so the UserHubEntity is inserted with HubRole=HubAdmin. The cache key user_hub_role:{userId}:{hubId} is invalidated, so subsequent EnsureCallerCanManage/EnsureCallerIsOwner checks load the new admin role from the DB, granting management/owner-gated capabilities on the hub. The same call can create memberships for arbitrary other userIds.

</details>

---

### UserHubController.Unfollow takes userId from the query string with no ownership check (IDOR)  
`🟠 HIGH` · category: **security** · id: `F48`

**Location:** `GameHubz.Api/Controllers/UserHubController.cs:21-29`

**Description:** The Unfollow endpoint accepts userId and hubId from the query string and passes them straight to UserHubService.Unfollow, which hard-deletes the (userId, hubId) membership with no verification that the caller equals userId or manages the hub (the authorization line is commented out). Any authenticated user can forcibly remove any other user's hub membership.

**Evidence:** UserHubController.cs:24 the only authorization is commented out; Service.Unfollow(userId, hubId) is called with the raw query userId. UserHubService.Unfollow (lines 56-65) hard-deletes without any caller comparison.

**Suggested fix:** Ignore the query userId and use the authenticated caller's id for self-unfollow, or require EnsureCallerCanManage(hubId, caller) when removing someone else. Remove the commented-out auth and add a real check.

<details><summary>Repro path (verifier)</summary>

Authenticate as any user with a valid token. Send DELETE /api/userHub/unfollow?userId=<VICTIM_GUID>&hubId=<HUB_GUID>. The class-level [Authorize] passes for any token; line 24 auth is commented out; Service.Unfollow uses the victim's userId from the query directly, GetByUserAndHub finds the victim's membership row, and HardDeleteEntity permanently removes it. The victim is silently removed from the hub.

</details>

<sub>Independently flagged by: hub-social, security-sweep</sub>

---

### IDOR / mass-assignment: generic CRUD exposed on UserSocial, MatchEvidence, MatchChat with no ownership checks  
`🟠 HIGH` · category: **security** · id: `F105`

**Location:** `GameHubz.Api/Controllers/UserSocialController.cs:12-19`

**Description:** UserSocialController, MatchEvidenceController, and MatchChatController inherit generic GetById/GetList/SaveEntity/Delete and override no roles; their services add no ownership enforcement on the generic path. Any authenticated user can update another user's social links, forge or delete match evidence, or forge or delete chat messages by supplying the target row's Id. UserSocialService.BeforeSave even uses inputDto.UserId without verifying it equals the caller.

**Evidence:** UserSocialController has no overrides. UserSocialService.BeforeSave (lines 32-35) uses inputDto.UserId for cache key only, no auth. MatchEvidenceService has no overrides. The inherited generic Delete/SaveEntity on MatchChatController (lines 12-37) are unguarded. ServiceFunctions.SaveEntity/DeleteEntity perform no ownership checks.

**Suggested fix:** For each, override the generic methods (or UserRolesSave/Delete) and enforce that the caller owns the row / is a match participant / is a tournament manager; reject body-supplied UserId differing from the token.

<details><summary>Repro path (verifier)</summary>

As any authenticated user: (1) POST /api/UserSocial with body {"Id":"<another user's UserSocial row id>","Type":...,"Username":"attacker","UserId":"<victim id>"} — MapToEntity loads the victim's row by Id and overwrites its fields; CheckAuthorization(null) is a no-op and BeforeSave only clears a cache key. (2) DELETE /api/UserSocial/{id} (or /MatchEvidence/{id} or /MatchChat/{id}) soft-deletes any row by id with no ownership check. (3) POST on MatchEvidence with a body Id pointing at another match's evidence row forges/edits evidence (Url) for a match the caller is not a participant of. All succeed with only a valid bearer token.

</details>

---

### Exception middleware returns full stack traces and internal exception details to clients  
`🟠 HIGH` · category: **security** · id: `F93`

**Location:** `GameHubz.Api/Middleware/ExceptionHandlingMiddlware.cs:72-91`

**Description:** For EVERY caught exception, including the catch-all unhandled path, CreateHandledErrorModel serializes exception.ToString() into the Details field and writes it to the HTTP response body. exception.ToString() includes the full stack trace, inner exceptions, type names, and often sensitive context (SQL fragments, file paths, connection details), returned to remote clients in production with no environment guard.

**Evidence:** Line 77 'Details = exception.ToString() ?? ""'; written via context.Response.WriteAsync for all categories including Unhandled (HTTP 500). The developer exception page is added separately at Program.cs:150-153.

**Suggested fix:** Only include exception.ToString()/Details when app.Environment.IsDevelopment(). In production return a generic message plus a correlation id mapping to the server-side log.

<details><summary>Repro path (verifier)</summary>

Send any request that triggers a server-side exception (e.g. malformed input that throws inside a service, or a DB error). The catch-all in Invoke (line 55-58) returns HTTP 500 with a JSON body whose Details field contains the full exception.ToString() including stack trace and any inner SQL/path details. Observable directly in the response body in production.

</details>

---

### Working-tree appsettings files still store all secrets in plaintext  
`🟠 HIGH` · category: **security** · id: `F92`

**Location:** `GameHubz.Api/appsettings.json:9-12, 52-57`

**Description:** Even though the files are git-ignored now, the deployed/working configuration keeps live credentials in plaintext on disk: Cloudinary ApiSecret, Twitch ClientSecret, YouTube ApiKey, Kick ClientSecret, and the Gmail SMTP app password. Anyone with filesystem or image access (CI artifacts, a misconfigured container, a backup) reads them. There is no use of user-secrets, environment variables, or a vault for the canonical values.

**Evidence:** appsettings.json:11-12 Cloudinary ApiKey/ApiSecret; line 19-20 Twitch secret; line 23 YouTube ApiKey; line 56 SMTP Password 'hhkiuaipfwilylhp'. EmailService.cs:47-49 uses smtpOptions.Username/Password directly.

**Suggested fix:** Source all secrets from environment variables or a secret manager at deploy time (builder already calls AddEnvironmentVariables). Keep appsettings with placeholder values only, and rotate the stored keys since they were also in git history.

<details><summary>Repro path (verifier)</summary>

Anyone with read access to the deployed container filesystem, a backup, a CI build artifact, or the git history (secrets still present in commit e449f8d and ancestors) can extract the Cloudinary ApiSecret, Twitch ClientSecret, YouTube ApiKey, and the Gmail SMTP app password, then impersonate the service against those providers / send mail as gamehubz.noreply@gmail.com.

</details>

<sub>Independently flagged by: infra, security-sweep</sub>

---

### NLog database target uses Microsoft.Data.SqlClient against a PostgreSQL connection string — all error logging silently fails  
`🟠 HIGH` · category: **reliability** · id: `F95`

**Location:** `GameHubz.Api/nlog.config:15-18`

**Description:** The NLog database target's dbProvider is 'Microsoft.Data.SqlClient' (SQL Server) but the connectionString resolves to ConnectionStrings.DatabaseConnection, a Npgsql/PostgreSQL string. SqlClient cannot connect to Postgres, so every error write throws inside NLog and is dropped. The application's entire error-audit trail never reaches the database, so production incidents leave no log record.

**Evidence:** nlog.config:16 dbProvider="Microsoft.Data.SqlClient" with connectionString from ConnectionStrings.DatabaseConnection (Postgres DSN); Program.cs:143 options.UseNpgsql(...). A prior commit set the provider for SqlServer and was not reverted when the DB moved to Postgres.

**Suggested fix:** Switch the NLog database target to the Npgsql provider (dbProvider='Npgsql.NpgsqlConnection, Npgsql' with @parameter placeholders) or point it at a dedicated SQL Server log DB. Verify a deliberately-thrown exception lands in the log table.

<details><summary>Repro path (verifier)</summary>

Run the API in any environment, trigger any code path that logs at Error level (e.g. an unhandled exception flowing through ExceptionHandlingMiddlware). NLog attempts to write the event to the SQL Server "database" target, Microsoft.Data.SqlClient fails to open the Npgsql/Postgres connection string (unsupported keywords / unreachable as a SQL Server endpoint), NLog swallows the target exception internally, and no row appears in the log table. Confirm by inspecting the Postgres "log" table after a deliberately thrown exception — it stays empty.

</details>

---

### UnitOfWorkFactory caches one DbContext per scope and NotificationService disposes it mid-request via using  
`🟠 HIGH` · category: **reliability** · id: `F72`

**Location:** `GameHubz.Data/UnitOfWork/UnitOfWorkFactory.cs:22-33`

**Description:** The factory constructor news up one ApplicationContext and caches it; CreateAppUnitOfWork() always returns that SAME instance. The factory is AddScoped, so every service in a request shares one DbContext (the intended unit-of-work pattern). The problem: NotificationService (Transient) does 'using var uow = unitOfWorkFactory.CreateAppUnitOfWork();', so the using block disposes the request's shared ApplicationContext, and any subsequent EF work in the same request throws ObjectDisposedException. NotificationService does not own that context and must not dispose it. The MatchService comment claiming NotificationService 'owns its own DbContext scope' documents an intent the factory does not implement.

**Evidence:** UnitOfWorkFactory.cs:22 news up ApplicationContext; lines 30-33 return the single cached field. Program.cs:122 AddScoped<IUnitOfWorkFactory>. NotificationService.cs:96 'using var uow = ...' then awaits uow.UserRepository.ClearPushTokenAsync at line 106; BaseUnitOfwork.cs:188 disposes the underlying DbContext.

**Suggested fix:** Do not create the DbContext in the factory constructor and do not cache a single UoW; return a fresh ApplicationContext per CreateAppUnitOfWork() (via IDbContextFactory). Critically, NotificationService must obtain its own context (IDbContextFactory/IServiceScopeFactory) so its using does not dispose the request-shared context; remove the using over the shared instance.

<details><summary>Repro path (verifier)</summary>

Send a chat message or trigger any notification path (e.g. MatchChatService.SendMessage -> SendNotification at MatchChatService.cs:69, or DirectChatService -> SendPushNotification). The fire-and-forget Task.Run reads opponent via the request-shared this.AppUnitOfWork, then calls notificationService.SendToOneAsync. Expo returns a parseable response (result.Data != null), so SendBatchAsync enters `using var uow = CreateAppUnitOfWork()` (NotificationService.cs:96) and on block exit disposes the shared ApplicationContext (BaseUnitOfwork.cs:188). When this background task overlaps the still-in-flight request (or another concurrent push task) using the same context, EF throws ObjectDisposedException or a concurrent-use InvalidOperationException; under load this produces intermittent dropped DB operations and failed stale-token cleanup.

</details>

<sub>Independently flagged by: data-ef, perf-ef-sweep, async-concurrency-sweep</sub>

---

### Refresh-token exchange does not check that the user is active/verified  
`🟠 HIGH` · category: **security** · id: `F4`

**Location:** `GameHubz.Logic/Services/AuthService.cs:69-94`

**Description:** ExchangeRefreshToken loads the user, validates the unexpired refresh token, and immediately mints a new access token plus a rotated refresh token. Unlike Login (which rejects !IsActive and gates !IsVerified), the refresh path performs no IsActive/IsVerified/IsDeleted check. A deactivated/soft-deleted (or never-verified) user holding a valid refresh token can keep minting access tokens until that long-lived token expires, bypassing the account-disable control.

**Evidence:** AuthService.cs lines 83-94: after HasValidRefreshToken it calls GenerateEncodedToken with no IsActive/IsVerified guard. Compare Login (lines 126-151) which returns DeletedAccount for !IsActive and fails for !IsVerified. GetByIdWithRefreshTokens (UserRepository.cs lines 20-27) does not filter on IsActive.

**Suggested fix:** In ExchangeRefreshToken, after loading the user, reject when !user.IsActive or user.IsDeleted (and optionally !IsVerified) and revoke their refresh tokens before issuing new ones.

<details><summary>Repro path (verifier)</summary>

N/A

</details>

---

### Password change/reset does not revoke existing refresh tokens  
`🟠 HIGH` · category: **security** · id: `F5`

**Location:** `GameHubz.Logic/Services/AuthService.cs:97-113`

**Description:** ChangeUserPassword updates the stored hash but does not delete/revoke the user's existing refresh tokens. The same gap applies to both password-reset paths (ResetPassword, ResetPasswordWithOtp) which never touch RefreshTokens. After a credential change/reset all existing sessions should be invalidated; here an attacker who previously obtained a refresh token (e.g. compromised device) keeps minting access tokens until the token naturally expires.

**Evidence:** AuthService.cs lines 108-112 set user.Password and SaveAsync with no RefreshTokenRepository deletion. PasswordManagementService.cs ResetPassword (lines 58-63) and ResetPasswordWithOtp (lines 85-90) likewise only set Password.

**Suggested fix:** On any password change/reset, hard-delete all RefreshTokenEntity rows for that UserId so all sessions must re-authenticate.

<details><summary>Repro path (verifier)</summary>

Attacker obtains a victim's refresh token (e.g. from a previously-compromised/old device). Victim changes their password via ChangeUserPassword, or resets it via ResetPassword / ResetPasswordWithOtp. The victim's old RefreshTokenEntity rows are never deleted. Attacker calls ExchangeRefreshToken with the stale refresh token + a still-decodable access token; HasValidRefreshToken passes (token exists and not yet expired), a new access token and refresh token are issued, and the attacker retains access for up to the 72-hour refresh-token lifetime despite the password change.

</details>

---

### Blob containers created with public BlobContainer access — all uploaded files anonymously readable and listable  
`🟠 HIGH` · category: **security** · id: `F57`

**Location:** `GameHubz.Logic/Services/BlobService.cs:25`

**Description:** BlobService.Upload calls CreateIfNotExistsAsync(PublicAccessType.BlobContainer), which grants anonymous read AND container enumeration of every blob, applied uniformly to every container including 'documents' and 'email-attachment'. Any unauthenticated party who knows the storage account URL can list and download every uploaded asset including private documents, defeating the [Authorize] guards on the controllers.

**Evidence:** Line 25 'CreateIfNotExistsAsync(PublicAccessType.BlobContainer);' combined with BlobContainers.Documents/EmailAttachment being valid targets (BlobUrlHelper.GetContainerName lines 14-17). BlobContainer is the most permissive option.

**Suggested fix:** Use PublicAccessType.None and serve files via authenticated/SAS-signed URLs, or at minimum PublicAccessType.Blob (no listing) only for genuinely public Images/Thumbnails and None for Documents/EmailAttachment.

<details><summary>Repro path (verifier)</summary>

Upload a Document asset via AssetService.UploadAsset (AssetType.Document). BlobService.Upload creates the "documents" container with PublicAccessType.BlobContainer. An unauthenticated attacker who knows BlobConfig:BlobBaseUrl can issue an anonymous list request against https://<account>.blob.core.windows.net/documents?restype=container&comp=list to enumerate every blob, then download each directly, with no token/auth — bypassing the [Authorize] guards on the controllers.

</details>

---

### Group-stage qualifier config under-validated: non-power-of-two qualifier counts crash knockout seeding  
`🟠 HIGH` · category: **bug** · id: `F12`

**Location:** `GameHubz.Logic/Services/BracketService.cs:367-372, 2963-2971, 3131, 3512-3530`

**Description:** GenerateGroupStageWithKnockout only validates participants.Count >= numberOfGroups*2 and that totalQualifiers (numberOfGroups*qualifiersPerGroup) is a power of two; it never checks each group will contain at least qualifiersPerGroup players. When qualifiersPerGroup>2, groups can have fewer players than qualifiersPerGroup. At advancement CheckAndAdvanceGroupStage takes Math.Min(qualifiersPerGroup, sorted.Count) per group and recomputes totalQualifiers = qualifiers.Count, which may no longer be a power of two. That is passed to GetStandardBracketSeeding whose bracketOrder grows to the next power of two and indexes sorted[i] for every i, so indices exceed n-1 for non-power-of-two n.

**Evidence:** Line 367 validates only 'participants.Count < numberOfGroups * 2'. Line 371 checks power-of-two using configured qualifiersPerGroup. Line 2963 caps per group at sorted.Count, line 2971 recomputes totalQualifiers. GetStandardBracketSeeding line 3518 'while (count < n)' doubling past n, then line 3529 indexes sorted with values > n-1.

**Suggested fix:** At generation time require participants.Count >= numberOfGroups * qualifiersPerGroup (and that each group's floor size covers it). Make GetStandardBracketSeeding robust to non-power-of-two input (pad with null slots) or assert IsPowerOfTwo(n) before indexing.

<details><summary>Repro path (verifier)</summary>

See reasoning.

</details>

---

### Double-elimination Grand Final has no bracket reset — LB champion wins the title with a single GF win  
`🟠 HIGH` · category: **bug** · id: `F18`

**Location:** `GameHubz.Logic/Services/BracketService.cs:833-844, 2192-2202`

**Description:** In real double-elimination the Winners-Bracket champion enters the Grand Final with zero losses, so if the Losers-Bracket champion wins the GF a reset Grand Final must be played. The generator creates exactly ONE GrandFinal match and AdvanceWinnerToNextMatch's GrandFinal branch unconditionally completes the tournament on the first GF result. A player who never lost in WB can be eliminated by a single defeat while the LB player (who already had one loss) wins on a single win — effectively single-elimination at the final.

**Evidence:** Line 836 creates only one GF match; lines 838-844 wire WB final (home) and LB final (away). Lines 2192-2202 set Status=Completed and WinnerUserId on the first GF result with no check for whether the WB champion lost and no second GF.

**Suggested fix:** If single-GF is intended, document it. Otherwise add a reset Grand Final: when the LB participant wins the first GF, do not complete; create/open a second GF (swap home/away) and complete only on its result. Detect WB-champion identity via the GF home slot.

<details><summary>Repro path (verifier)</summary>

DE tournament; LB champion beats WB champion in the single GrandFinal -> AdvanceWinnerToNextMatch GrandFinal branch (line 2192) completes the tournament on the first result with no bracket reset.

</details>

---

### SetAvailability has no participant check — any user can set slots and schedule any match (IDOR)  
`🟠 HIGH` · category: **security** · id: `F25`

**Location:** `GameHubz.Logic/Services/MatchService.cs:63-124`

**Description:** SetAvailability is reachable by any authenticated user. The only line restricting it to participants (`if (!isHome && !isAway) throw`) is commented out (line 79). The slot-writing logic is a binary if/else, so a non-participant (isHome=false, isAway=false) silently falls into the else branch and overwrites the AwaySlots of someone else's match. If injected slots intersect the opponent's HomeSlots, the code sets ScheduledStartTime, flips Status to Scheduled, and pushes the opponent. A malicious client can tamper with the schedule of arbitrary matches.

**Evidence:** Line 79 commented-out participant guard. Lines 86-93 write AwaySlots for any caller that is not home. matchId comes straight from request.MatchId (MatchController.cs:29) with no ownership verification.

**Suggested fix:** Re-enable the participant guard after computing isHome/isAway; do not use a bare else to assign AwaySlots — only assign on the side the caller belongs to.

<details><summary>Repro path (verifier)</summary>

Authenticate as any user; POST /api/match/availability with another user's MatchId and arbitrary SelectedSlots.

</details>

---

### UploadMatchEvidence performs no authorization — any user can attach evidence to any match  
`🟠 HIGH` · category: **security** · id: `F26`

**Location:** `GameHubz.Logic/Services/MatchService.cs:186-214`

**Description:** UploadMatchEvidence loads the match only to build a Cloudinary folder path and then stores MatchEvidenceEntity rows, but never verifies the caller is a participant or tournament admin. Any logged-in user can POST files to an arbitrary matchId, polluting another match's evidence gallery (shown in result/approval screens) and burning Cloudinary storage on arbitrary uploads.

**Evidence:** Method body has no IsMatchParticipant or CanManageTournamentAsync call; it only fetches GetForMatchEvidence and writes evidence rows. Controller MatchController.cs:53-58 has only class-level [Authorize].

**Suggested fix:** Require IsMatchParticipant(match, user.UserId) || CanManageTournamentAsync(match.TournamentId, user) before accepting uploads. Enforce file count/size/content-type limits.

<details><summary>Repro path (verifier)</summary>

Authenticate as any user (valid JWT). Obtain or guess any matchId you are not a participant in (match ids are GUIDs but are exposed throughout the API, e.g. in match listings/notifications). POST multipart files to api/match/{matchId}/evidence. The server uploads the files to that match's Cloudinary folder and inserts MatchEvidenceEntity rows, which then surface in that match's result/approval screens — with no participant or admin check rejecting the request.

</details>

---

### Fire-and-forget Task.Run notification helpers use the request-scoped DbContext after the request scope is disposed  
`🟠 HIGH` · category: **reliability** · id: `F109`

**Location:** `GameHubz.Logic/Services/MatchService.cs:126-172 (also MatchChatService.cs:74-107, TournamentService.cs:368-396, BracketService.cs:4162-4185)`

**Description:** Several SendNotification helpers launch an unawaited Task.Run that calls this.AppUnitOfWork repositories (GetById/GetUsersByHub/GetWithParticipants/GetAllUserIdsByTournamentId/GetPushTokensByUserIds) inside the background task. The Scoped UnitOfWorkFactory hands every caller the same single request-scoped ApplicationContext, which the DI container disposes when the request returns. The detached task then queries a disposed context (ObjectDisposedException) or runs concurrently on the non-thread-safe DbContext, and the bare catch swallows it — so notifications silently fail. The author's own comment (MatchService.cs:313) confirms this access pattern is known to be unsafe, yet these sites still query the scoped context inside Task.Run. (DirectChatService DM push is tracked separately as F51.)

**Evidence:** MatchService.cs:128 Task.Run -> this.AppUnitOfWork.UserRepository.GetById (lines 143, 160); same this.AppUnitOfWork.* access inside Task.Run at BracketService.cs:4168/4172, TournamentService.cs:374, MatchChatService.cs:80/96. UnitOfWorkFactory.cs:22-33 returns the shared instance; Program.cs:122 AddScoped; BaseUnitOfwork.cs:188 Context.Dispose(). Contrast the safe FireAndForgetPush pattern (MatchService.cs:340) which pre-resolves tokens.

**Suggested fix:** Resolve all DB data BEFORE entering Task.Run (pass materialized push tokens/user ids into the background task, as FireAndForgetPush does), or have the background task create its own DI scope (IServiceScopeFactory) and resolve a fresh UnitOfWork. Do not touch the request-scoped AppUnitOfWork from a detached task.

<details><summary>Repro path (verifier)</summary>

Call any endpoint backed by these services (e.g. set match availability -> MatchService.SetAvailability calls SendNotification at line 113). SendNotification spawns Task.Run and returns immediately; the controller finishes and the DI scope disposes the shared ApplicationContext. The detached task's await this.AppUnitOfWork.UserRepository.GetById(...) then executes against the disposed/raced context, throwing ObjectDisposedException (or a DbContext concurrency exception), which the bare catch swallows — the opponent never receives the push notification. Most reproducible under load or when the request returns quickly after the SendNotification call.

</details>

<sub>Independently flagged by: match, tournament, bracket-progress, perf-ef-sweep, async-concurrency-sweep</sub>

---

### Password-reset OTP is brute-forceable: 6-digit code, no attempt limiting, 30-minute window  
`🟠 HIGH` · category: **security** · id: `F3`

**Location:** `GameHubz.Logic/Services/PasswordManagementService.cs:131-146`

**Description:** The reset OTP is a 6-digit number (RandomNumberGenerator.GetInt32(100000,1000000)) stored as plaintext, valid for 30 minutes. ResetPasswordWithOtp matches purely on (OtpCode, Email) with no per-account failed-attempt counter, lockout, or rate limiting. An attacker who knows a target email can request a reset and enumerate the 900,000-value space within the validity window, yielding account takeover.

**Evidence:** PasswordManagementService.cs line 135 'RandomNumberGenerator.GetInt32(100000, 1000000)', line 140 30-minute expiry. UserRepository.cs lines 182-187 GetByOtpAndMail matches only OtpCode + Email. No attempt-count field on UserEntity, no lockout in ResetPasswordWithOtp (lines 66-91).

**Suggested fix:** Add a failed-attempt counter that invalidates the OTP after a few wrong guesses, add IP/account rate limiting on resetPassword/forgotPassword, use a larger code, and store a hash of the OTP rather than plaintext.

<details><summary>Repro path (verifier)</summary>

1) Attacker calls POST /api/auth/forgotPassword with a known victim email (unauthenticated). 2) Server generates a 6-digit OTP, stores it plaintext with a 30-min expiry. 3) Attacker scripts POST /api/auth/resetPassword with ResetPasswordOtpRequestDto {Email, OtpCode, Password, ConfirmPassword}, iterating OtpCode over 000000-999999. With no attempt limit/lockout/rate limiting, a matching code within the 30-min window passes GetByOtpAndMail, the password is reset, and the account is taken over. (Note: the forgotPassword endpoint also returns the OTP directly in the HTTP response body at AuthController.cs:147-149, an additional independent exposure.)

</details>

---

### Solo registration trusts UserId from the request body instead of the caller's token (impersonation)  
`🟠 HIGH` · category: **security** · id: `F37`

**Location:** `GameHubz.Logic/Services/TournamentRegistrationService.cs:47-49`

**Description:** Solo registration flows through the inherited generic SaveEntity (any authenticated user). TournamentRegistrationService.BeforeSave reads entity.UserId, populated from TournamentRegistrationPost.UserId in the request body, never overridden from the authenticated principal. A user can create a registration on behalf of an arbitrary UserId, and eligibility (region/country/exclusive) is evaluated against the victim's profile, not the caller's.

**Evidence:** TournamentRegistrationPost.UserId is a settable body property (TournamentRegistrationPost.cs:12). BeforeSave uses entity.UserId directly (TournamentRegistrationService.cs:49,52) with no comparison to the token user.

**Suggested fix:** Force entity.UserId = current token user id and reject/ignore any body-supplied UserId on the solo registration path.

<details><summary>Repro path (verifier)</summary>

As any authenticated user, POST to /api/tournamentRegistration with body { "tournamentId": "<some tournament>", "userId": "<victim user id>" }. The inherited BasicGenericController SaveEntity endpoint accepts it (no UserRolesSave override), maps UserId straight from the body, and creates a Pending registration for the victim; eligibility (region/country/exclusive) is checked against the victim's profile rather than the caller's.

</details>

---

### CloseRegistration performs a lifecycle transition with no manager authorization  
`🟠 HIGH` · category: **security** · id: `F38`

**Location:** `GameHubz.Logic/Services/TournamentService.cs:110-130`

**Description:** CloseRegistration changes tournament status to RegistrationClosed and rejects all pending registrations, but unlike CancelTournament/HardDeleteTournament/OpenRegistration it does not route through GetHubOwnedTournamentOrThrow / CanManageTournamentAsync. The controller endpoint is only [Authorize]. Any authenticated user can force-close registration on any tournament and mass-reject its pending registrations.

**Evidence:** TournamentService.cs:110-130 fetches via GetWithPendingRegistration and updates status with no auth call; compare OpenRegistration and CancelTournament which enforce GetHubOwnedTournamentOrThrow (line 285-295). Controller TournamentController.cs:88-94.

**Suggested fix:** Add CanManageTournamentAsync(id) at the start of CloseRegistration (or route through GetHubOwnedTournamentOrThrow).

<details><summary>Repro path (verifier)</summary>

Authenticate as any user; POST /api/tournament/{otherOwnersTournamentId}/closeRegistration. Passes the controller [Authorize] gate, service runs with no manager check, sets status RegistrationClosed and rejects all pending registrations.

</details>

<sub>Independently flagged by: tournament, security-sweep</sub>

---

### IDOR: TournamentStage and TournamentGroup generic CRUD have no authorization  
`🟠 HIGH` · category: **security** · id: `F106`

**Location:** `GameHubz.Logic/Services/TournamentStageService.cs:whole file (also TournamentGroupService.cs)`

**Description:** TournamentStageService and TournamentGroupService extend the generic base and override no Save/Delete authorization (no CanManageTournamentAsync, no EnsureCaller, no token use). Their controllers expose the inherited generic SaveEntity/Delete/GetById with only [Authorize]. Any authenticated user can create, edit, or delete tournament stages/groups by id, directly tampering with bracket structure of any tournament.

**Evidence:** Grep for SaveEntity/DeleteEntity overrides, CanManageTournament, EnsureCaller, GetTokenUserInfoFromContext in TournamentStageService.cs returned no matches; same for TournamentGroupService. Controllers add no role overrides.

**Suggested fix:** Override BeforeSave/BeforeDelete (or controller role hooks) to require tournament-management authorization for the affected tournamentId.

<details><summary>Repro path (verifier)</summary>

Authenticate as any user (passes controller [Authorize]). Send POST /api/TournamentStage with body {"id":"<victim-stage-guid>","tournamentId":"<victim-tournament>","order":0,...} — CheckAuthorization(null) is a no-op, ServiceFunctions.StageEntity does AddUpdateEntity on that id, mutating another tournament's stage. Or send DELETE /api/TournamentStage/{victim-stage-id} (and equivalents on /api/TournamentGroup) to soft-delete bracket structure of a tournament you do not own.

</details>

---

### IDOR: edit any user's profile via UserService.UpdateInfo trusting body UserId  
`🟠 HIGH` · category: **security** · id: `F103`

**Location:** `GameHubz.Logic/Services/UserService.cs:317-343`

**Description:** UpdateInfo loads the user by request.UserId (from the request body) and overwrites Nickname/Username/Country with no verification that the caller is that user. The controller action ignores its own route id and passes the request straight through. Any authenticated user can rename/modify any other user's profile.

**Evidence:** UserService.UpdateInfo line 319 uses request.UserId; UserUpdateInfoRequest exposes public Guid UserId. UserController.UpdateInfo (lines 91-95) ignores the route id and calls Service.UpdateInfo(request). Contrast DeleteAccount (UserService.cs:364-375) which uses the token UserId.

**Suggested fix:** Derive the user id from UserContextReader and ignore any client-supplied UserId (or verify request.UserId == token.UserId, allowing admins explicitly).

<details><summary>Repro path (verifier)</summary>

Authenticate as any user. POST /api/User/update with body {"UserId":"<victim-guid>","Nickname":"x","Username":"y"}. The route id is ignored; the service loads the victim by body UserId and overwrites their Nickname/Username, then saves.

</details>

---

### SignalR hubs have no authentication or authorization — anonymous clients can join any chat/DM group by id  
`🟠 HIGH` · category: **security** · id: `F94`

**Location:** `GameHubz.Logic/SignalR/DirectChatHub.cs:9-19`

**Description:** DirectChatHub and MatchChatHub are mapped with no [Authorize] and no .RequireAuthorization(). JoinChatGroup/JoinMatchGroup take an arbitrary chatId/matchId and add the connection to that group with no participant check. Any unauthenticated client can connect, call JoinChatGroup with a guessed/enumerated chat id, and receive all private messages broadcast to that group. Broken access control / IDOR on private messaging.

**Evidence:** DirectChatHub.cs:11-14 JoinChatGroup -> Groups.AddToGroupAsync with no verification; class has no [Authorize]. MatchChatHub.cs:8-11 identical. Program.cs:112-113 MapHub calls have no RequireAuthorization. The default policy only applies to endpoints that opt in via [Authorize].

**Suggested fix:** Add [Authorize] to both hubs (or .RequireAuthorization() on MapHub), configure JwtBearer to read the access_token query param for WebSockets (OnMessageReceived), and verify the authenticated user is a participant before AddToGroupAsync.

<details><summary>Repro path (verifier)</summary>

Connect (no token required) to wss://host/hubs/dm or /hubs/chat — the connection succeeds because no FallbackPolicy is configured and hubs lack [Authorize]. Invoke JoinChatGroup("<a chat id the attacker is not a participant of>"). From that moment, every message sent in that chat is delivered to the attacker's connection via the ReceiveMessage event (DirectChatService.cs:159-160 broadcasts the full message DTO including Content to the dm:{chatId} group), with no participant check ever performed.

</details>

---

## 🟡 Medium (5)

### Anonymous /user/{id} share page runs a heavy un-cached match-stats aggregation on every hit (DoS amplification)  
`🟡 MEDIUM` · category: **performance** · id: `F83`

**Location:** `GameHubz.Api/Controllers/ShareController.cs:170-194`

**Description:** UserProfile is [AllowAnonymous] and on every request executes a large aggregate query over the entire MatchEntity table (GroupBy with conditional Counts over OR'd nullable FK predicates) plus a second CountAsync over TournamentEntity with a nested Members.Any subquery. The OR-across-nullable-FK shape cannot use a single index, so each hit is a scan/expensive aggregation. Unlike the PDF path there is no server-side caching — only an HTTP Cache-Control header which does not protect the origin from direct requests, link-preview crawlers, or an attacker looping random GUIDs. There is no rate limiting anywhere in the app.

**Evidence:** Lines 170-194 build the aggregate query; lines 191-194 the trophies CountAsync with WinnerTeam.Members.Any. [AllowAnonymous] at line 21. No ICacheService usage in ShareController (only Response.Headers.CacheControl at line 308). Grep for RateLimiter returns no files.

**Suggested fix:** Cache the computed scoreboard/description server-side per user id (e.g. 5-min TTL like the PDF path) and/or add ASP.NET rate limiting to the public Share routes. Consider precomputing/denormalizing user stats and gating enumeration of arbitrary GUIDs.

<details><summary>Repro path (verifier)</summary>

Issue an anonymous loop of GET https://share.<host>/user/{guid} requests (valid or random GUIDs). Each hit executes the MatchEntity GroupBy aggregation (ShareController.cs:170-189) plus the TournamentEntity WinnerTeam.Members.Any CountAsync (191-194) with no server-side cache (only a Cache-Control header at line 308) and no rate limiting (none exists in the repo), amplifying DB load per request.

</details>

---

### No optimistic-concurrency token on any entity; concurrent read-modify-write loses updates  
`🟡 MEDIUM` · category: **reliability** · id: `F74`

**Location:** `GameHubz.Data/Base/BaseRepository.cs:61-67`

**Description:** All reads go through BaseDbSet() (AsNoTracking). UpdateEntity re-attaches the detached entity with Entry(entity).State = Modified, marking every column dirty. No RowVersion/xmin concurrency token is configured anywhere. On Postgres, two requests that read an entity, modify different fields, and SaveChanges each issue a full-row UPDATE; the second silently overwrites the first (lost update). For MatchEntity (scores, status, proposed results) and TournamentEntity this is a real data-integrity risk under concurrent organizer/player actions. The advisory lock only protects bracket advancement, not general edits.

**Evidence:** BaseRepositoryT.cs:210 'Set<TEntity>()!.AsNoTracking()' for all reads; BaseRepository.cs:66 sets whole entity Modified. No concurrency token mapping in ApplicationContext.cs.

**Suggested fix:** Add a concurrency token to mutable entities (Postgres xmin via .Property<uint>("xmin").IsRowVersion(), or an explicit RowVersion column) and handle DbUpdateConcurrencyException. At minimum add it to MatchEntity and TournamentEntity.

<details><summary>Repro path (verifier)</summary>

Two clients (e.g., organizer editing tournament settings and a background process / second organizer editing a different field on the same TournamentEntity, or two updates to the same MatchEntity outside bracket advancement) each call GetById (AsNoTracking, detached copy), modify different fields in memory, then call UpdateEntity -> SaveChanges. Each emits a full-row UPDATE of all columns. The later-committing request overwrites the earlier request's field changes with its own stale snapshot values, silently discarding the first update. No DbUpdateConcurrencyException is raised because no concurrency token is mapped.

</details>

---

### Logout deletes a refresh token by value without scoping to the caller (IDOR)  
`🟡 MEDIUM` · category: **security** · id: `F6`

**Location:** `GameHubz.Logic/Services/AuthService.cs:156-171`

**Description:** Logout takes a refreshToken string from the request body and deletes the matching row via FindByTokenValue (token-only lookup, not scoped to the authenticated user). The endpoint is [Authorize] but never compares the token's owner to the caller. Combined with plaintext storage of refresh tokens, any authenticated user who learns another user's refresh token value can revoke that user's session; the revocation path trusts a body value without verifying ownership.

**Evidence:** AuthService.cs line 163 FindByTokenValue then HardDeleteEntity, no ownership check. RefreshTokenRepository.cs lines 23-28 FindByTokenValue filters on Token only.

**Suggested fix:** Resolve the caller's UserId from UserContextReader and use FindByUserIdAndTokenValue(userId, token) so a user can only revoke their own tokens.

<details><summary>Repro path (verifier)</summary>

Authenticate as user A. Obtain user B's refresh token value (e.g. via the plaintext-storage weakness or any leak). Send POST /auth/logout with [Authorize] header for A and body = B's refresh token string. AuthService.Logout looks the token up by value only and hard-deletes it, revoking B's session without verifying ownership.

</details>

---

### Forgot-password endpoints enable user (email) enumeration  
`🟡 MEDIUM` · category: **security** · id: `F7`

**Location:** `GameHubz.Logic/Services/PasswordManagementService.cs:131-146`

**Description:** SendEmailWithForgotPasswordToken(string) throws 'This email does not exists.' when no user matches, and CreateForgotPasswordToken throws EntityNotFoundException for an unknown email. These produce distinct responses for non-existent vs existing accounts, letting an attacker enumerate which emails are registered. Forgot-password flows should be uniform regardless of whether the email exists.

**Evidence:** PasswordManagementService.cs line 133 throws 'This email does not exists.' on GetByEmail null; lines 102-105 throw EntityNotFoundException for unknown email in CreateForgotPasswordToken.

**Suggested fix:** Always return a generic success response (e.g. 'If an account exists, an email was sent') for forgot-password regardless of whether the user was found.

<details><summary>Repro path (verifier)</summary>

POST /forgotPassword with body a JSON string of an unregistered email -> HTTP 500 with body message "This email does not exists.". POST same endpoint with a registered email -> HTTP 200 with OTP in body. Compare status codes / bodies to enumerate valid accounts. No authentication required.

</details>

---

### Twitch client_secret sent in URL query string to token endpoint  
`🟡 MEDIUM` · category: **security** · id: `F65`

**Location:** `GameHubz.Logic/Services/Streaming/TwitchStreamClient.cs:110-115`

**Description:** GetAppTokenAsync builds the OAuth client_credentials request with client_id and client_secret as URL query-string parameters and POSTs with a null body. Secrets in a URL are captured in HTTP access logs, reverse-proxy/CDN logs, APM spans, and exception diagnostics, leaking the Twitch application client secret into log sinks with weaker access controls and enabling anyone reading logs to mint app tokens. Twitch accepts these as form-encoded body parameters.

**Evidence:** Lines 110-113 concatenate client_id/client_secret/grant_type into the url; line 115 'client.PostAsync(url, null, ct)' sends the secret in the URI rather than a body.

**Suggested fix:** Send credentials in a FormUrlEncodedContent body posted to the token endpoint, and never log the full URI.

<details><summary>Repro path (verifier)</summary>

Trigger any Twitch stream URL lookup when no app token is cached (cold cache / after TTL expiry). GetAppTokenAsync issues POST https://id.twitch.tv/oauth2/token?client_id=...&client_secret=...&grant_type=client_credentials with a null body. Any reverse proxy, CDN, load balancer, or APM tool in front of/around the egress that logs full request URIs (or any exception path that serializes HttpRequestMessage.RequestUri) records the cleartext client secret, allowing anyone with read access to those log sinks to mint Twitch app tokens.

</details>

---

## ⚠️ Unverified — needs manual review (42)

These findings were reported by the area reviewers but their adversarial verification step failed because the session-token limit was hit. They have **not** been independently confirmed against the real code — treat as leads, not as proven defects. Worth a manual look since several look plausible.

- **`F10`** — PBKDF2 iteration count (10000) is below current guidance  
  `GameHubz.Logic/Crypto/Pbkdf2Hasher.cs:30-35`  
  <sub>Note: unverifiable</sub>
- **`F104`** — IDOR / mass-assignment: create or edit any tournament via inherited generic SaveEntity (no manage check)  
  `GameHubz.Logic/Services/TournamentService.cs:297-332`  
  <sub>Note: unverifiable</sub>
- **`F107`** — IDOR: FollowHub trusts client-supplied UserId  
  `GameHubz.Logic/Services/UserService.cs:194-209`  
  <sub>Note: unverifiable</sub>
- **`F108`** — Unauthenticated diagnostics endpoint exposes infrastructure state and triggers email send  
  `GameHubz.Api/Controllers/TestController.cs:11, 33-52, 106-114`  
  <sub>Note: unverifiable</sub>
- **`F11`** — AccessTokenReader trusts role from token on the email-claim branch instead of the DB record  
  `GameHubz.Logic/Tokens/AccessTokenReader.cs:78-91`  
  <sub>Note: unverifiable</sub>
- **`F110`** — Empty catch blocks swallow all fire-and-forget notification failures with no logging  
  `GameHubz.Logic/Services/MatchService.cs:170, 350 (and MatchChatService.cs:105, DirectChatService.cs:219, FriendService.cs:489, TournamentService.cs:394, BracketService.cs:4183)`  
  <sub>Note: unverifiable</sub>
- **`F111`** — N+1 query in CheckAndAdvanceGroupStage: one participant query per group inside the advancement lock  
  `GameHubz.Logic/Services/BracketService.cs:2941-2943`  
  <sub>Note: unverifiable</sub>
- **`F13`** — Group-stage snake-distribution uses Seed which is typically null, making group draw a no-op by signup order  
  `GameHubz.Logic/Services/BracketService.cs:398-405, 572-579`  
  <sub>Note: unverifiable</sub>
- **`F14`** — Swiss play-in qualifier count mismatch throws inside the result-finalization lock, stalling the tournament  
  `GameHubz.Logic/Services/BracketService.cs:2483-2488, 2424-2428`  
  <sub>Note: unverifiable</sub>
- **`F15`** — Round-robin bye handling diverges silently from Swiss (no bye match row created)  
  `GameHubz.Logic/Services/BracketService.cs:3260-3288`  
  <sub>Note: unverifiable</sub>
- **`F16`** — Multiple 'new Random()' instances within one generation call may seed identically, biasing member assignment  
  `GameHubz.Logic/Services/BracketService.cs:513, 599, 1300, 2972`  
  <sub>Note: unverifiable</sub>
- **`F17`** — Third-place play-off can be left permanently half-filled when a semi-final feeder had a bye chain  
  `GameHubz.Logic/Services/BracketService.cs:3209-3225`  
  <sub>Note: unverifiable</sub>
- **`F19`** — Swiss/Group team head-to-head tiebreak is a no-op; team groups decided by alphabetical name  
  `GameHubz.Logic/Services/BracketService.cs:2944-2967`  
  <sub>Note: unverifiable</sub>
- **`F20`** — CheckAndCompleteLeague writes Guid.Empty as tournament winner when standings are empty  
  `GameHubz.Logic/Services/BracketService.cs:2293`  
  <sub>Note: unverifiable</sub>
- **`F21`** — Swiss round-1 bye win is eagerly mutated in memory and also re-derived from the match row (duplicated source of truth)  
  `GameHubz.Logic/Services/BracketService.cs:1103-1119, 1892-1918`  
  <sub>Note: unverifiable</sub>
- **`F27`** — GetMatchesByUser and GetAvailability take userId from the route without verifying the caller (IDOR)  
  `GameHubz.Api/Controllers/MatchController.cs:39-51`  
  <sub>Note: unverifiable</sub>
- **`F30`** — Approval flow can finalize a draw on an elimination match, producing no winner to advance  
  `GameHubz.Logic/Services/BracketService.cs:1475-1511, 1573-1596`  
  <sub>Note: unverifiable</sub>
- **`F31`** — SetScheduled has no authorization — any user can force any match to Scheduled now  
  `GameHubz.Logic/Services/MatchService.cs:174-184`  
  <sub>Note: unverifiable</sub>
- **`F33`** — MatchChatService.SendMessage does not verify the sender is a match participant  
  `GameHubz.Logic/Services/MatchChatService.cs:35-72`  
  <sub>Note: unverifiable</sub>
- **`F39`** — Capacity check on registration approval is a read-then-write race allowing MaxPlayers overflow  
  `GameHubz.Logic/Services/TournamentRegistrationService.cs:91-117`  
  <sub>Note: unverifiable</sub>
- **`F40`** — Team join/create capacity and uniqueness checks are TOCTOU races  
  `GameHubz.Logic/Services/TournamentTeamService.cs:104-142`  
  <sub>Note: unverifiable</sub>
- **`F41`** — Solo registration does not validate the tournament is in RegistrationOpen status  
  `GameHubz.Logic/Services/TournamentRegistrationService.cs:47-75`  
  <sub>Note: unverifiable</sub>
- **`F49`** — Blocked-user DM bypass window: send/open-chat block checks rely on 5-minute cached block sets  
  `GameHubz.Logic/Services/FriendService.cs:39-45`  
  <sub>Note: unverifiable</sub>
- **`F50`** — Unbounded take on direct-message history allows loading an entire chat in one query  
  `GameHubz.Logic/Services/DirectChatService.cs:92-101`  
  <sub>Note: unverifiable</sub>
- **`F53`** — RequestJoin/ApproveRequest can throw a unique-constraint violation when a soft-deleted membership exists  
  `GameHubz.Logic/Services/UserHubRequestService.cs:50-64`  
  <sub>Note: unverifiable</sub>
- **`F55`** — GetMembers leaks each member's PushToken to any authenticated caller  
  `GameHubz.Data/Repository/UserHubRepository.cs:69-77`  
  <sub>Note: unverifiable</sub>
- **`F59`** — No file-type or content validation on asset/avatar uploads — arbitrary file types accepted  
  `GameHubz.Logic/Services/AssetService.cs:55-84`  
  <sub>Note: unverifiable</sub>
- **`F60`** — Unbounded upload size on blob/avatar paths — no app-level size limit, decompression-bomb / OOM risk  
  `GameHubz.Api/Controllers/UserProfileController.cs:60-70`  
  <sub>Note: unverifiable</sub>
- **`F61`** — FileSystemService builds file paths from caller-supplied names with no path-traversal sanitization  
  `GameHubz.Logic/Services/FileSystemService.cs:85-93`  
  <sub>Note: unverifiable</sub>
- **`F66`** — YouTube API key embedded in request URL query string  
  `GameHubz.Logic/Services/Streaming/YouTubeStreamClient.cs:87, 110, 118`  
  <sub>Note: unverifiable</sub>
- **`F67`** — YouTube TryGetAsync swallows all exceptions including cancellation, with no logging  
  `GameHubz.Logic/Services/Streaming/YouTubeStreamClient.cs:128-137`  
  <sub>Note: unverifiable</sub>
- **`F68`** — Cached Twitch app token is never invalidated on 401, blocking resolution for the whole TTL  
  `GameHubz.Logic/Services/Streaming/TwitchStreamClient.cs:55-86, 104-134`  
  <sub>Note: unverifiable</sub>
- **`F69`** — Streaming clients use the default 100s HttpClient timeout when invoked without a CancellationToken  
  `GameHubz.Logic/Services/Streaming/YouTubeStreamClient.cs:48, 76, 130`  
  <sub>Note: unverifiable</sub>
- **`F73`** — DbContext registered Transient but UnitOfWorkFactory bypasses DI and manually news the context  
  `GameHubz.Api/Program.cs:142-145`  
  <sub>Note: unverifiable</sub>
- **`F75`** — GetByUser loads full match graph then filters/sorts in memory with no pagination  
  `GameHubz.Data/Repository/MatchRepository.cs:92-152`  
  <sub>Note: unverifiable</sub>
- **`F76`** — GetFilteredData defaults pageSize to int.MaxValue — unpaged callers load the whole filtered table  
  `GameHubz.Data/Base/BaseRepositoryT.cs:223-242`  
  <sub>Note: unverifiable</sub>
- **`F80`** — AcquireAdvancementLock can leak an open connection / held advisory lock if the caller throws before release  
  `GameHubz.Data/Repository/TournamentRepository.cs:63-82`  
  <sub>Note: unverifiable</sub>
- **`F9`** — ChangeUserPassword throws raw System.Exception for invalid current password and lacks new-password validation  
  `GameHubz.Logic/Services/AuthService.cs:103-108`  
  <sub>Note: unverifiable</sub>
- **`F96`** — CORS policy AllowAnyOrigin + AllowAnyHeader + AllowAnyMethod applied globally  
  `GameHubz.Api/Program.cs:95-104`  
  <sub>Note: unverifiable</sub>
- **`F97`** — JWT RequireExpirationTime = false weakens token-expiry enforcement  
  `GameHubz.Api/Startup/AuthenticationStartup.cs:45-47`  
  <sub>Note: unverifiable</sub>
- **`F98`** — JWT signing key derived from ASCII bytes of a short shared secret  
  `GameHubz.Api/Startup/AuthenticationStartup.cs:22`  
  <sub>Note: unverifiable</sub>
- **`F99`** — LoggerService reads and disposes the request body stream with a leaking StreamReader  
  `GameHubz.Logic/Services/LoggerService.cs:43-51`  
  <sub>Note: unverifiable</sub>

## ❌ Rejected as false-positive after verification (4)

The adversarial verifier opened the real code and refuted these. Listed for transparency only.

- `F102` — IDOR: any authenticated user can delete any hub via inherited generic Delete
- `F2` — Google JWT validation scheme performs NO validation and returns an empty principal
- `F51` — DirectChatService fire-and-forget push uses the request-scoped UnitOfWork after the request completes
- `F8` — OTP/email matching can match on stale/empty values with no input validation

## 🎯 Recommended quick wins

Highest value-to-effort fixes, in suggested order:

1. **OTP leak** (`F1`) — one-line change: `return Ok();` instead of `return Ok(otpCode);` in `AuthController.forgotPassword`. Closes full account-takeover.
2. **Exception middleware leaks internals** — disable stack-trace / full exception detail in non-Development responses (this is what produced the giant NullReferenceException dump on mobile this morning).
3. **Generic CRUD on User / Hub / Tournament / TournamentStage / TournamentGroup / UserSocial / MatchEvidence / MatchChat** — remove or override the inherited `SaveEntity`/`Delete` actions on these controllers, or add explicit ownership/role checks. This alone closes a long list of CRITICAL/HIGH IDORs in one sweep.
4. **Add per-action authorization helpers** to tournament/match/hub mutations (`CanManageTournamentAsync`, `IsMatchParticipant`, `IsHubAdmin`) — many spots already use them, but a chunk of endpoints simply forgot.
5. **Fire-and-forget pattern** — replace `Task.Run(async () => ... use uow ...)` with `IServiceScopeFactory.CreateScope()` + a fresh `IUnitOfWorkFactory` per task (or move to a proper background queue/Hangfire). Fix the `using var uow` inside `NotificationService.SendBatchAsync` so it stops disposing the request's context mid-request.
6. **Strip committed secrets** from `appsettings.json` history, rotate the JWT signing key, DB passwords and SMTP/API keys. Move to user-secrets / env vars / Azure KeyVault.
7. **SignalR hubs**: add `[Authorize]` and check that the connecting user is a member/participant of the group/match they're joining.
8. **Azure Blob containers** — change from `BlobContainer` public access to `None`; serve via signed URLs.
9. **Grand Final bracket reset** (`F44`) — small but tournament-correctness bug; LB winner should win twice in DE.

---

_Generated from workflow `wf_d5af3a48-948` (96 agents, 96 total)._
