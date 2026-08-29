-- fix_group_names.sql
--
-- Popravlja "legacy" nazive grupa. Stari generator je posle Z prelazio na sirove brojeve, pa je
-- turnir sa 32 grupe dobijao "Group A" … "Group Z", "Group 27" … "Group 32" — dve šeme imenovanja
-- u istoj listi, a numerisane grupe su se (cifre se sortiraju pre slova) lepile na pogrešan kraj.
--
-- Novo pravilo (isto kao GroupNaming.cs na backendu): ako grupa ima do 26, imenuju se slovima
-- A–Z, a ako ih ima više, cela faza ide brojevima 1, 2, 3, … Zato se za turnir preko 26 grupa
-- preimenuju SVE grupe, i one koje sad imaju slovo:
--
--   Group A  -> Group 1        Group Z  -> Group 26
--   Group B  -> Group 2        Group 27 -> Group 27   (brojevi ostaju isti)
--
-- Za turnir od 32 grupe to znači 26 preimenovanja (A–Z), dok grupe 27–32 ostaju iste.
-- Turniri sa 26 ili manje grupa se NE diraju.
--
-- BEZBEDNO: menja se samo TournamentGroup."Name". Redosled grupa se NE menja — mapiranje ide
-- tačno po postojećem redosledu (A->1, B->2, …), pa žreb za knockout ostaje identičan.
-- Skripta je idempotentna: drugo pokretanje ne radi ništa.
--
-- KAKO POKRENUTI: sve tri naredbe su samostalne i atomične — radi i u psql-u
-- (-f fix_group_names.sql) i u pgAdmin / DBeaver (Execute script), sa autocommit-om ili bez.
-- Korak 1 je samo pregled, pusti korak 2 tek kad ti se lista svidi.
--
--   >>> GUID TURNIRA STOJI NA 3 MESTA (blok "WITH scope AS" u koraku 1, 2 i 3) — promeni SVA TRI. <<<
--   Za sve turnire odjednom, u sva tri koraka zameni
--       SELECT '01a02eed-…'::uuid AS tournament_id
--   sa
--       SELECT "Id" FROM "Tournament" WHERE "IsDeleted" = false
--
-- AKO DOBIJEŠ "current transaction is aborted" (25P02): to nije greška ove skripte nego
-- zaostala transakcija u sesiji — neka ranija naredba je pukla i sve posle nje se ignoriše.
-- Uradi ROLLBACK (u DBeaver-u dugme Rollback ili disconnect/connect) pa pusti skriptu ponovo.
--
-- PAŽNJA, DBeaver u manual-commit režimu: posle koraka 2 moraš da pritisneš COMMIT,
-- inače preimenovanje ostaje samo u tvojoj transakciji i nikad ne stigne do baze.
--
-- POSLE POKRETANJA: bracket cache (Redis, TTL 5 min) još drži stare nazive —
-- sačekaj do 5 minuta ili refreshuj turnir u aplikaciji dvaput.


-- ============================================================================
-- 1) PREGLED — šta će biti promenjeno (ne menja ništa)
-- ============================================================================
WITH scope AS (
    SELECT '01a02eed-94de-72b3-9c9c-ffe45abd0dd8'::uuid AS tournament_id
),
group_rename AS (
    -- Grupe jedne faze, u redosledu u kom ih backend čita (GroupNaming.SortKey: prvo kraća
    -- oznaka, pa abecedno — tako se i "1, 2, … 10" i "A, B, … Z" sortiraju ispravno).
    SELECT g."Id"           AS group_id,
           s."TournamentId" AS tournament_id,
           g."Name"         AS old_name,
           'Group ' || row_number() OVER (
               PARTITION BY g."TournamentStageId"
               ORDER BY length(regexp_replace(g."Name", '^Group ', '', 'i')),
                        regexp_replace(g."Name", '^Group ', '', 'i')
           ) AS new_name,
           count(*) OVER (PARTITION BY g."TournamentStageId") AS groups_in_stage
    FROM "TournamentGroup" g
    JOIN "TournamentStage" s  ON s."Id" = g."TournamentStageId"
    JOIN scope             sc ON sc.tournament_id = s."TournamentId"
    WHERE g."IsDeleted" = false
      AND s."IsDeleted" = false
)
SELECT t."Name"          AS "Turnir",
       r.groups_in_stage AS "BrojGrupa",
       r.old_name        AS "StariNaziv",
       r.new_name        AS "NoviNaziv"
FROM group_rename r
JOIN "Tournament" t ON t."Id" = r.tournament_id
WHERE r.groups_in_stage > 26
  AND r.old_name <> r.new_name
ORDER BY r.tournament_id, length(r.new_name), r.new_name;


-- ============================================================================
-- 2) PREIMENOVANJE  (jedna atomična naredba)
-- ============================================================================
WITH scope AS (
    SELECT '01a02eed-94de-72b3-9c9c-ffe45abd0dd8'::uuid AS tournament_id
),
group_rename AS (
    SELECT g."Id"           AS group_id,
           g."Name"         AS old_name,
           'Group ' || row_number() OVER (
               PARTITION BY g."TournamentStageId"
               ORDER BY length(regexp_replace(g."Name", '^Group ', '', 'i')),
                        regexp_replace(g."Name", '^Group ', '', 'i')
           ) AS new_name,
           count(*) OVER (PARTITION BY g."TournamentStageId") AS groups_in_stage
    FROM "TournamentGroup" g
    JOIN "TournamentStage" s  ON s."Id" = g."TournamentStageId"
    JOIN scope             sc ON sc.tournament_id = s."TournamentId"
    WHERE g."IsDeleted" = false
      AND s."IsDeleted" = false
)
UPDATE "TournamentGroup" g
   SET "Name"       = r.new_name,
       "ModifiedOn" = (now() AT TIME ZONE 'utc')
FROM group_rename r
WHERE g."Id" = r.group_id
  AND r.groups_in_stage > 26
  AND r.old_name <> r.new_name;


-- ============================================================================
-- 3) PROVERA — kako grupe izgledaju posle, u redosledu u kom ih app prikazuje
-- ============================================================================
WITH scope AS (
    SELECT '01a02eed-94de-72b3-9c9c-ffe45abd0dd8'::uuid AS tournament_id
)
SELECT t."Name" AS "Turnir",
       g."Name" AS "NoviNaziv",
       count(p."Id") AS "Ucesnika"
FROM "TournamentGroup" g
JOIN "TournamentStage" s  ON s."Id" = g."TournamentStageId"
JOIN "Tournament"      t  ON t."Id" = s."TournamentId"
JOIN scope             sc ON sc.tournament_id = s."TournamentId"
LEFT JOIN "TournamentParticipant" p
       ON p."TournamentGroupId" = g."Id" AND p."IsDeleted" = false
WHERE g."IsDeleted" = false
  AND s."IsDeleted" = false
GROUP BY t."Name", g."Id", g."Name"
ORDER BY t."Name",
         length(replace(g."Name", 'Group ', '')),
         replace(g."Name", 'Group ', '');
