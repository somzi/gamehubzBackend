-- reset_tournament_bracket.sql
--
-- Briše ceo bracket (Match, TeamMatch, TournamentGroup, TournamentStage) za jedan turnir
-- i vraća tournament Status u RegistrationClosed (2), tako da možeš ručno da okineš
-- CreateBracket ponovo (TryClaimBracketGeneration prolazi za sve osim InProgress/Completed).
--
-- Učesnici (TournamentParticipant) se NE brišu — samo se reseta TournamentGroupId
-- i sve statistike (Points/Wins/Losses/Draws/GoalsFor/GoalsAgainst/GroupPosition).
--
-- KAKO POKRENUTI (psql):
--   1) Otvori fajl, promeni vrednost ispod (između navodnika) u GUID turnira.
--   2) psql -h <host> -p <port> -U <user> -d <db> -f reset_tournament_bracket.sql
--
-- ALI: ako koristiš pgAdmin / DBeaver / drugi GUI klijent, samo zameni :tid u celom
-- fajlu sa literalom '00000000-0000-0000-0000-000000000000'::uuid i pokreni kao plain SQL.

\set tid '\'REPLACE-WITH-TOURNAMENT-GUID\'::uuid'

BEGIN;

-- Sanity check: turnir mora da postoji
SELECT "Id", "Status", "IsTeamTournament", "Format"
FROM "Tournament"
WHERE "Id" = :tid;

-- 1) Deca Match-a (Evidence, Chat, Stream)
DELETE FROM "MatchEvidence"
WHERE "MatchId" IN (SELECT "Id" FROM "Match" WHERE "TournamentId" = :tid);

DELETE FROM "MatchChat"
WHERE "MatchId" IN (SELECT "Id" FROM "Match" WHERE "TournamentId" = :tid);

DELETE FROM "MatchStream"
WHERE "MatchId" IN (SELECT "Id" FROM "Match" WHERE "TournamentId" = :tid);

-- 2) Razveži self-reference FK-ove pre brisanja (DeleteBehavior.Restrict u modelu).
--    Match: NextMatchId, NextMatchLoserBracketId, WinnerParticipantId, TeamMatchId.
UPDATE "Match"
   SET "NextMatchId" = NULL,
       "NextMatchLoserBracketId" = NULL,
       "WinnerParticipantId" = NULL,
       "TeamMatchId" = NULL
 WHERE "TournamentId" = :tid;

--    TeamMatch: NextTeamMatchId, NextTeamMatchLoserBracketId, WinnerTeamParticipantId.
UPDATE "TeamMatch"
   SET "NextTeamMatchId" = NULL,
       "NextTeamMatchLoserBracketId" = NULL,
       "WinnerTeamParticipantId" = NULL
 WHERE "TournamentId" = :tid;

-- 3) Match prvo (sub-mečevi linkovani na TeamMatch su u istoj tabeli),
--    pa onda TeamMatch.
DELETE FROM "Match"     WHERE "TournamentId" = :tid;
DELETE FROM "TeamMatch" WHERE "TournamentId" = :tid;

-- 4) Otkači učesnike sa grupa i resetuj standings.
UPDATE "TournamentParticipant"
   SET "TournamentGroupId" = NULL,
       "GroupPosition"     = NULL,
       "Points"            = 0,
       "Wins"              = 0,
       "Losses"            = 0,
       "Draws"             = 0,
       "GoalsFor"          = 0,
       "GoalsAgainst"      = 0
 WHERE "TournamentId" = :tid;

-- 5) Grupe i stage-ovi
DELETE FROM "TournamentGroup"
 WHERE "TournamentStageId" IN (SELECT "Id" FROM "TournamentStage" WHERE "TournamentId" = :tid);

DELETE FROM "TournamentStage"
 WHERE "TournamentId" = :tid;

-- 6) Vrati status na RegistrationClosed (2) i očisti winner-e, pa CreateBracket može opet da claim-uje.
UPDATE "Tournament"
   SET "Status"        = 2,
       "WinnerUserId"  = NULL,
       "WinnerTeamId"  = NULL
 WHERE "Id" = :tid;

-- Provera posle: trebalo bi 0 mečeva, 0 stage-ova, 0 grupa, status = 2
SELECT
    (SELECT COUNT(*) FROM "Match"            WHERE "TournamentId" = :tid) AS matches_left,
    (SELECT COUNT(*) FROM "TeamMatch"        WHERE "TournamentId" = :tid) AS team_matches_left,
    (SELECT COUNT(*) FROM "TournamentStage"  WHERE "TournamentId" = :tid) AS stages_left,
    (SELECT COUNT(*) FROM "TournamentGroup"  WHERE "TournamentStageId" IN
        (SELECT "Id" FROM "TournamentStage" WHERE "TournamentId" = :tid)) AS groups_left,
    (SELECT "Status" FROM "Tournament" WHERE "Id" = :tid) AS tournament_status;

-- Ako brojevi izgledaju OK → COMMIT;
-- Ako nešto smrdi → ROLLBACK;
-- (Skripta je već u BEGIN bloku; završi je ručno sa COMMIT ili ROLLBACK.)
