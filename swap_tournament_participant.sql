-- swap_tournament_participant.sql
--
-- Zamenjuje jednog igrača drugim NA JEDNOM TURNIRU, tako da novi igrač NASLEDI sve
-- odigrane rezultate (mečeve, skorove, poene u grupi, poziciju u bracket-u).
--
-- ZAŠTO JE OVO DOVOLJNO (i zašto je nasleđivanje najlakša varijanta):
--   Solo mečevi NE pamte UserId na Match-u — Match."HomeUserId"/"AwayUserId" su NULL za
--   solo mečeve i popunjeni su samo za timske pod-mečeve. Sve ostalo (bracket, standings,
--   statistika na profilu, head-to-head, moji mečevi) resolve-uje igrača kroz
--   TournamentParticipant."UserId" (vidi MatchRepository.GetStatsByUserId,
--   GetLastMatchesByUserId, GetPerformanceByUserId*, GetHeadToHead).
--   => Promena UserId-a na TOM JEDNOM redu prebacuje ceo istorijat na novog igrača.
--      Match tabelu ne diramo uopšte (osim timskih pod-mečeva, sekcija 2.3).
--
-- NEMA unique constraint-a na (TournamentId, UserId) — samo obični indeks — pa baza
-- neće da vrisne ako novi igrač već postoji na turniru. Zato sekcija 1 to proverava
-- RUČNO. Ako pre-check kaže da već postoji, PREKINI (ROLLBACK), inače dobijaš igrača
-- dva puta u istom turniru.
--
-- KAKO POKRENUTI:
--   Radi u psql, pgAdmin, DBeaver — bez \set varijabli. Promeni samo 3 GUID-a ispod.
--   Skripta je u BEGIN bloku i NE commit-uje sama — pogledaj rezultate pa ručno
--   COMMIT; ili ROLLBACK;
--
-- POSLE COMMIT-a OBAVEZNO OČISTI REDIS (inače stari username visi do isteka TTL-a):
--   DEL tournament:019f8670-626c-78b1-95e8-48dc888a39b8
--   DEL bracket:019f8670-626c-78b1-95e8-48dc888a39b8
--   DEL bracket:v3:019f8670-626c-78b1-95e8-48dc888a39b8
--   DEL league_standings:019f8670-626c-78b1-95e8-48dc888a39b8
--   DEL pdf:bracket:019f8670-626c-78b1-95e8-48dc888a39b8
--   DEL player_stats:<stari> player_stats_v2:<stari> user_profile:<stari>
--   DEL player_stats:<novi>  player_stats_v2:<novi>  user_profile:<novi>
--   (user_matches:* i user_profile_tournaments:* imaju 30s–1min TTL, prođu sami)

BEGIN;

-- ═══════════════════════════════════════════════════════════════════════════
-- PARAMETRI — jedino mesto gde se menjaju vrednosti
-- ═══════════════════════════════════════════════════════════════════════════
CREATE TEMP TABLE _swap (tid uuid, old_user uuid, new_user uuid) ON COMMIT DROP;

INSERT INTO _swap VALUES (
    '019f8670-626c-78b1-95e8-48dc888a39b8'::uuid,  -- TURNIR
    '019e6578-3960-739e-a4a4-c834823c96b0'::uuid,  -- STARI igrač (izlazi)
    '019fa2c8-f91c-79af-b81b-e5b0b062a106'::uuid   -- NOVI igrač (ulazi)
);


-- ═══════════════════════════════════════════════════════════════════════════
-- 1) PRE-CHECK — pogledaj SVE ovo pre nego što pustiš sekciju 2
-- ═══════════════════════════════════════════════════════════════════════════

-- 1.1 Turnir postoji? Solo ili timski? U kom je statusu?
--     (Status: 0 Draft, 1 RegOpen, 2 RegClosed, 3 InProgress, 4 Completed, 5 Cancelled)
SELECT t."Id", t."Name", t."Status", t."IsTeamTournament", t."Format", t."HubId",
       t."WinnerUserId", t."WinnerTeamId", h."Name" AS hub_name, h."Id" AS hub_id
  FROM "Tournament" t
  LEFT JOIN "Hub" h ON h."Id" = t."HubId"
 WHERE t."Id" = (SELECT tid FROM _swap);

-- 1.2 Oba igrača postoje?  Očekivano: 2 reda.
SELECT u."Id", u."Username", u."Nickname", u."Email", u."IsDeleted",
       CASE WHEN u."Id" = (SELECT old_user FROM _swap) THEN 'STARI (izlazi)' ELSE 'NOVI (ulazi)' END AS role
  FROM "User" u
 WHERE u."Id" IN (SELECT old_user FROM _swap UNION SELECT new_user FROM _swap);

-- 1.3 Participant red starog igrača.  Očekivano: TAČNO 1 red.
--     Ako je 0 → igrač nije učesnik ovog turnira, stao si.
SELECT p."Id" AS participant_id, p."UserId", p."Seed", p."TournamentGroupId", p."GroupPosition",
       p."Points", p."Wins", p."Draws", p."Losses", p."GoalsFor", p."GoalsAgainst", p."TeamId"
  FROM "TournamentParticipant" p
 WHERE p."TournamentId" = (SELECT tid FROM _swap)
   AND p."UserId"       = (SELECT old_user FROM _swap)
   AND p."IsDeleted"    = false;

-- 1.4 !!! BLOKER !!! Novi igrač NE SME već biti učesnik ovog turnira.
--     Očekivano: 0 redova. Ako ima red → ROLLBACK, ovo nije swap nego dupliranje.
SELECT 'BLOKER: novi igrac je vec ucesnik ovog turnira' AS problem,
       p."Id" AS participant_id, p."Seed", p."TournamentGroupId", p."Points"
  FROM "TournamentParticipant" p
 WHERE p."TournamentId" = (SELECT tid FROM _swap)
   AND p."UserId"       = (SELECT new_user FROM _swap)
   AND p."IsDeleted"    = false;

-- 1.5 Registracije oba igrača na ovom turniru.
--     (Status: 1 Pending, 2 Approved, 3 Rejected)
--     Očekivano: 1 red za starog (Approved), 0 za novog. Ako novi VEĆ ima red,
--     preskoči 2.2 i umesto toga njegov red prebaci u Approved (2), a starom stavi
--     "IsDeleted" = true.
SELECT r."Id", r."UserId", r."Status", r."TeamId", r."IsDeleted",
       CASE WHEN r."UserId" = (SELECT old_user FROM _swap) THEN 'STARI' ELSE 'NOVI' END AS role
  FROM "TournamentRegistration" r
 WHERE r."TournamentId" = (SELECT tid FROM _swap)
   AND r."UserId" IN (SELECT old_user FROM _swap UNION SELECT new_user FROM _swap);

-- 1.6 Šta se to zapravo nasleđuje — mečevi starog igrača na ovom turniru.
--     MatchStatus: 1 Pending, 2 Scheduled, 3 Live, 4 Completed, 5 NoShow
SELECT m."Status",
       COUNT(*)                                                        AS broj_meceva,
       COUNT(*) FILTER (WHERE m."WinnerParticipantId" = p."Id")        AS pobede,
       COUNT(*) FILTER (WHERE m."WinnerParticipantId" IS NOT NULL
                          AND m."WinnerParticipantId" <> p."Id")       AS porazi,
       COUNT(*) FILTER (WHERE m."Status" = 4
                          AND m."WinnerParticipantId" IS NULL)         AS remiji
  FROM "Match" m
  JOIN "TournamentParticipant" p
    ON p."Id" IN (m."HomeParticipantId", m."AwayParticipantId")
 WHERE m."TournamentId"  = (SELECT tid FROM _swap)
   AND p."TournamentId"  = (SELECT tid FROM _swap)
   AND p."UserId"        = (SELECT old_user FROM _swap)
   AND p."IsDeleted"     = false
 GROUP BY m."Status"
 ORDER BY m."Status";

-- 1.7 Timski pod-mečevi (samo ako je "IsTeamTournament" = true iz 1.1).
--     Ovo su jedini mečevi gde Match pamti UserId direktno.
SELECT COUNT(*) AS timski_podmecevi_starog
  FROM "Match" m
 WHERE m."TournamentId" = (SELECT tid FROM _swap)
   AND m."TeamMatchId" IS NOT NULL
   AND (SELECT old_user FROM _swap) IN (m."HomeUserId", m."AwayUserId");

-- 1.8 Članstvo u hub-u. HubRole: 1=Owner, 2=Admin, 3=Member, 4=Exclusive
--     (provere su equality-based, vrednosti NISU poređane po privilegiji).
--     Ako je turnir "IsExclusive" = true iz 1.1, novi igrač mora imati UserHub red sa
--     "HubRole" IN (1, 2, 4) da bi mu se turnir prikazao u feed-u — vidi
--     UserHubRepository.GetHubIdsWithExclusiveAccess. Za običan turnir svaki red je OK.
--     Ako novi igrač uopšte nema red → vidi sekciju 3.1 (opciono).
SELECT uh."UserId", uh."HubRole", uh."IsDeleted",
       CASE WHEN uh."UserId" = (SELECT old_user FROM _swap) THEN 'STARI' ELSE 'NOVI' END AS role
  FROM "UserHub" uh
 WHERE uh."HubId" = (SELECT t."HubId" FROM "Tournament" t WHERE t."Id" = (SELECT tid FROM _swap))
   AND uh."UserId" IN (SELECT old_user FROM _swap UNION SELECT new_user FROM _swap);


-- ═══════════════════════════════════════════════════════════════════════════
-- 2) SWAP
-- ═══════════════════════════════════════════════════════════════════════════

-- 2.1 SRŽ SWAP-A: participant red menja vlasnika. Sve statistike, grupa, seed,
--     pozicija i svi mečevi (koji gledaju "HomeParticipantId"/"AwayParticipantId")
--     ostaju netaknuti — samo iza njih sad stoji drugi user.
UPDATE "TournamentParticipant"
   SET "UserId"     = (SELECT new_user FROM _swap),
       "ModifiedOn" = now() AT TIME ZONE 'utc'
 WHERE "TournamentId" = (SELECT tid FROM _swap)
   AND "UserId"       = (SELECT old_user FROM _swap)
   AND "IsDeleted"    = false;

-- 2.2 Registracija ide uz učesnika (da se brojevi prijavljenih i lista prijava slože).
--     PRESKOČI ako je 1.5 pokazao da novi igrač već ima registraciju na turniru.
UPDATE "TournamentRegistration"
   SET "UserId"     = (SELECT new_user FROM _swap),
       "ModifiedOn" = now() AT TIME ZONE 'utc'
 WHERE "TournamentId" = (SELECT tid FROM _swap)
   AND "UserId"       = (SELECT old_user FROM _swap)
   AND "IsDeleted"    = false;

-- 2.3 Timski pod-mečevi — jedino mesto gde Match direktno pamti igrača.
--     Za solo turnir ovo pogodi 0 redova (HomeUserId/AwayUserId su NULL), pa je
--     bezbedno pustiti u svakom slučaju.
UPDATE "Match"
   SET "HomeUserId" = (SELECT new_user FROM _swap),
       "ModifiedOn" = now() AT TIME ZONE 'utc'
 WHERE "TournamentId" = (SELECT tid FROM _swap)
   AND "HomeUserId"   = (SELECT old_user FROM _swap);

UPDATE "Match"
   SET "AwayUserId" = (SELECT new_user FROM _swap),
       "ModifiedOn" = now() AT TIME ZONE 'utc'
 WHERE "TournamentId" = (SELECT tid FROM _swap)
   AND "AwayUserId"   = (SELECT old_user FROM _swap);

-- 2.4 Članstvo u timu (samo timski turniri; 0 redova za solo).
UPDATE "TournamentTeamMember"
   SET "UserId"     = (SELECT new_user FROM _swap),
       "ModifiedOn" = now() AT TIME ZONE 'utc'
 WHERE "UserId" = (SELECT old_user FROM _swap)
   AND "TeamId" IN (SELECT tt."Id" FROM "TournamentTeam" tt
                     WHERE tt."TournamentId" = (SELECT tid FROM _swap));

-- 2.5 Kapiten tima (samo ako je stari igrač bio kapiten).
UPDATE "TournamentTeam"
   SET "CaptainUserId" = (SELECT new_user FROM _swap),
       "ModifiedOn"    = now() AT TIME ZONE 'utc'
 WHERE "TournamentId"   = (SELECT tid FROM _swap)
   AND "CaptainUserId"  = (SELECT old_user FROM _swap);

-- 2.6 Pobednik turnira (pogodi red samo ako je turnir već završen i stari je osvojio).
UPDATE "Tournament"
   SET "WinnerUserId" = (SELECT new_user FROM _swap),
       "ModifiedOn"   = now() AT TIME ZONE 'utc'
 WHERE "Id"           = (SELECT tid FROM _swap)
   AND "WinnerUserId" = (SELECT old_user FROM _swap);

-- 2.7 Predlozi rezultata koji čekaju odobrenje (RequireResultApproval turniri).
--     Bez ovoga bi "predložio: <stari>" ostalo zakucano na pending predlogu.
UPDATE "Match"
   SET "ProposedByUserId" = (SELECT new_user FROM _swap),
       "ModifiedOn"       = now() AT TIME ZONE 'utc'
 WHERE "TournamentId"     = (SELECT tid FROM _swap)
   AND "ProposedByUserId" = (SELECT old_user FROM _swap);

-- 2.8 Otvoreni admin-help zahtevi na mečevima ovog turnira.
UPDATE "Match"
   SET "AdminHelpRequestedByUserId" = (SELECT new_user FROM _swap),
       "ModifiedOn"                 = now() AT TIME ZONE 'utc'
 WHERE "TournamentId"               = (SELECT tid FROM _swap)
   AND "AdminHelpRequestedByUserId" = (SELECT old_user FROM _swap);

-- NAMERNO NE DIRAMO:
--   MatchChat."UserId"     — poruke je stvarno pisao stari igrač; prepis bi bio
--                            falsifikovanje istorije chata. Ako baš želiš da ih
--                            skloniš, soft-delete ih (blok u sekciji 3), ne prepisuj.
--   MatchChatRead."UserId" — samo "pročitano" markeri, bezopasno.
--   MatchEvidence          — vezan je na MatchId, nema UserId. Dokazi (screenshot-ovi)
--                            starog igrača ostaju na meču. To je i tačno: taj meč je
--                            odigran tako kako je odigran.
--   MatchStream            — "StreamerUserId" je zaseban feature, nije deo učešća.


-- ═══════════════════════════════════════════════════════════════════════════
-- 3) OPCIONO — otkomentariši samo ako ti pre-check kaže da treba
-- ═══════════════════════════════════════════════════════════════════════════

-- 3.1 Novi igrač nije član hub-a (1.8 mu nije vratio red).
--     HubRole: 1=HubOwner, 2=HubAdmin, 3=HubMember, 4=HubExclusive.
--     Ako je turnir "IsExclusive" = true → mora 4 (HubExclusive), inače 3 je dovoljno.
--     Ako red POSTOJI ali sa "HubRole" = 3 a turnir je exclusive → ne INSERT nego
--     UPDATE "UserHub" SET "HubRole" = 4 za tog usera i taj hub.
-- INSERT INTO "UserHub" ("Id", "UserId", "HubId", "HubRole", "CreatedOn", "IsDeleted")
-- SELECT gen_random_uuid(),
--        (SELECT new_user FROM _swap),
--        (SELECT t."HubId" FROM "Tournament" t WHERE t."Id" = (SELECT tid FROM _swap)),
--        3,
--        now() AT TIME ZONE 'utc',
--        false;

-- 3.2 Skloni chat poruke starog igrača sa mečeva ovog turnira (soft-delete).
-- UPDATE "MatchChat"
--    SET "IsDeleted" = true, "ModifiedOn" = now() AT TIME ZONE 'utc'
--  WHERE "UserId" = (SELECT old_user FROM _swap)
--    AND "MatchId" IN (SELECT m."Id" FROM "Match" m
--                       WHERE m."TournamentId" = (SELECT tid FROM _swap));


-- ═══════════════════════════════════════════════════════════════════════════
-- 4) POST-CHECK — pre COMMIT-a
-- ═══════════════════════════════════════════════════════════════════════════

-- 4.1 Stari igrač: 0 svuda. Novi igrač: 1 participant, 1 registracija.
SELECT
    (SELECT COUNT(*) FROM "TournamentParticipant"
      WHERE "TournamentId" = (SELECT tid FROM _swap)
        AND "UserId" = (SELECT old_user FROM _swap) AND "IsDeleted" = false)   AS stari_participant,   -- 0
    (SELECT COUNT(*) FROM "TournamentParticipant"
      WHERE "TournamentId" = (SELECT tid FROM _swap)
        AND "UserId" = (SELECT new_user FROM _swap) AND "IsDeleted" = false)   AS novi_participant,    -- 1
    (SELECT COUNT(*) FROM "TournamentRegistration"
      WHERE "TournamentId" = (SELECT tid FROM _swap)
        AND "UserId" = (SELECT old_user FROM _swap) AND "IsDeleted" = false)   AS stara_registracija,  -- 0
    (SELECT COUNT(*) FROM "TournamentRegistration"
      WHERE "TournamentId" = (SELECT tid FROM _swap)
        AND "UserId" = (SELECT new_user FROM _swap) AND "IsDeleted" = false)   AS nova_registracija,   -- 1
    (SELECT COUNT(*) FROM "Match"
      WHERE "TournamentId" = (SELECT tid FROM _swap)
        AND (SELECT old_user FROM _swap) IN ("HomeUserId", "AwayUserId"))      AS stari_podmecevi;     -- 0

-- 4.2 Nasleđeni istorijat sad visi na novom igraču — ovo su brojevi koje će videti
--     na profilu (Total / Wins / Losses iz OVOG turnira; profil sabira sve turnire).
SELECT u."Username", u."Nickname",
       p."Points", p."Wins", p."Draws", p."Losses", p."GoalsFor", p."GoalsAgainst",
       p."GroupPosition", p."Seed", p."TournamentGroupId",
       (SELECT COUNT(*) FROM "Match" m
         WHERE m."TournamentId" = (SELECT tid FROM _swap)
           AND m."Status" = 4
           AND p."Id" IN (m."HomeParticipantId", m."AwayParticipantId"))       AS zavrsenih_meceva
  FROM "TournamentParticipant" p
  JOIN "User" u ON u."Id" = p."UserId"
 WHERE p."TournamentId" = (SELECT tid FROM _swap)
   AND p."UserId"       = (SELECT new_user FROM _swap)
   AND p."IsDeleted"    = false;

-- 4.3 Bracket sanity: nijedan meč ovog turnira ne pokazuje na participant koji
--     ne postoji ili nije iz ovog turnira. Očekivano: 0 redova.
SELECT m."Id" AS match_id, m."RoundNumber", m."MatchOrder",
       m."HomeParticipantId", m."AwayParticipantId", m."WinnerParticipantId"
  FROM "Match" m
 WHERE m."TournamentId" = (SELECT tid FROM _swap)
   AND (
        (m."HomeParticipantId"   IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM "TournamentParticipant" p
             WHERE p."Id" = m."HomeParticipantId" AND p."TournamentId" = m."TournamentId"))
     OR (m."AwayParticipantId"   IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM "TournamentParticipant" p
             WHERE p."Id" = m."AwayParticipantId" AND p."TournamentId" = m."TournamentId"))
     OR (m."WinnerParticipantId" IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM "TournamentParticipant" p
             WHERE p."Id" = m."WinnerParticipantId" AND p."TournamentId" = m."TournamentId"))
   );

-- Ako 4.1 daje 0/1/0/1/0, 4.2 pokazuje novog igrača sa nasleđenim brojevima,
-- a 4.3 je prazan → COMMIT;
-- Bilo šta drugo → ROLLBACK;
