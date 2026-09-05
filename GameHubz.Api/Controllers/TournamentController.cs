using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace GameHubz.Api.Controllers
{
    [Route("api/tournament")]
    [ApiController]
    [Authorize]
    public class TournamentController : BasicGenericController<TournamentService, TournamentEntity, TournamentDto, TournamentPost, TournamentEdit>
    {
        private readonly BracketService bracketService;
        private readonly TournamentTeamService tournamentTeamService;
        private readonly TournamentExportService tournamentExportService;
        private readonly TournamentCsvExportService tournamentCsvExportService;
        private readonly ILocalizationService localizationService;

        public TournamentController(
            TournamentService service,
            AppAuthorizationService appAuthorizationService,
            BracketService bracketService,
            TournamentTeamService tournamentTeamService,
            TournamentExportService tournamentExportService,
            TournamentCsvExportService tournamentCsvExportService,
            ILocalizationService localizationService)
            : base(service, appAuthorizationService)
        {
            this.localizationService = localizationService;
            this.bracketService = bracketService;
            this.tournamentTeamService = tournamentTeamService;
            this.tournamentExportService = tournamentExportService;
            this.tournamentCsvExportService = tournamentCsvExportService;
        }

        [HttpPost("createBracket")]
        public async Task<IActionResult> CreateBracket([FromBody] CreateBracketRequest request)
        {
            await this.bracketService.CreateBracket(request);

            return Ok();
        }

        // Draw setup for the organiser's bracket picker: supported seeding modes, the shape to fill
        // (bracket size / byes, or groups + pots) and the entrants. Manager-gated inside the service.
        [HttpGet("{tournamentId}/draw/options")]
        public async Task<IActionResult> GetBracketDrawOptions(Guid tournamentId)
        {
            var options = await this.bracketService.GetDrawOptions(tournamentId);

            return Ok(options);
        }

        [HttpPost("{tournamentId}/bracket/reset")]
        public async Task<IActionResult> ResetKnockoutBracket(Guid tournamentId)
        {
            await this.bracketService.ResetKnockoutStage(tournamentId);

            return Ok();
        }

        [HttpPost("{tournamentId}/bracket/draw")]
        public async Task<IActionResult> DrawKnockoutBracket(Guid tournamentId)
        {
            await this.bracketService.DrawKnockoutFromGroups(tournamentId);

            return Ok();
        }

        [HttpPost("{tournamentId}/bracket/swap")]
        public async Task<IActionResult> SwapBracketParticipants(Guid tournamentId, [FromBody] SwapBracketParticipantsRequest request)
        {
            await this.bracketService.SwapBracketParticipants(tournamentId, request.ParticipantAId, request.ParticipantBId);

            return Ok();
        }

        [HttpGet("{tournamentId}/structure")]
        public async Task<IActionResult> GetTournamentStructure(Guid tournamentId)
        {
            var structure = await this.bracketService.GetTournamentStructure(tournamentId);

            return Ok(structure);
        }

        [HttpGet("{tournamentId}/structure/v2")]
        public async Task<IActionResult> GetTournamentStructureV2(Guid tournamentId)
        {
            var structure = await this.bracketService.GetTournamentStructureV2(tournamentId);

            return Ok(structure);
        }

        [HttpGet("{tournamentId}/structure/v3")]
        public async Task<IActionResult> GetTournamentStructureV3(Guid tournamentId)
        {
            var structure = await this.bracketService.GetTournamentStructureV3(tournamentId);

            return Ok(structure);
        }

        [HttpPost("matchResult")]
        public async Task<IActionResult> UpdateMatchResult([FromBody] MatchResultDto request)
        {
            await this.bracketService.UpdateMatchResult(request);

            // Return the freshly computed bracket so the mobile client can update its local
            // state directly without firing a follow-up GET /structure/v3. Approval / help
            // counts already flow to the client over the BadgesUpdated SignalR push, so we
            // don't need to include them here — that keeps this response focused on the one
            // piece of state the caller can't recover from the push (the bracket itself).
            var structure = await this.bracketService.GetTournamentStructureV3(request.TournamentId);
            return Ok(new { structure });
        }

        /// <summary>
        /// v2 of the result report: the whole best-of series, game by game. v1 stays as it is —
        /// it can only describe one game, and it cannot render the TieBreakRequired state a level
        /// knockout series produces, so series tournaments are reported exclusively through here.
        /// </summary>
        [HttpPost("v2/matchResult")]
        public async Task<IActionResult> UpdateMatchSeriesResult([FromBody] MatchSeriesResultDto request)
        {
            await this.bracketService.UpdateMatchSeriesResult(request);

            // Same contract as v1: hand back the recomputed bracket so the client doesn't need a
            // follow-up GET to reflect the result it just reported.
            var structure = await this.bracketService.GetTournamentStructureV3(request.TournamentId);
            return Ok(new { structure });
        }

        [HttpPost("matchResult/approve")]
        public async Task<IActionResult> ApproveMatchResult([FromBody] MatchResultDecisionRequest request)
        {
            await this.bracketService.ApproveProposedResult(request.MatchId);

            return Ok();
        }

        [HttpPost("matchResult/reject")]
        public async Task<IActionResult> RejectMatchResult([FromBody] MatchResultDecisionRequest request)
        {
            await this.bracketService.RejectProposedResult(request.MatchId);

            return Ok();
        }

        [HttpPost("matchResult/revert")]
        public async Task<IActionResult> RevertMatchResult([FromBody] MatchResultDecisionRequest request)
        {
            await this.bracketService.RevertMatchResult(request.MatchId, request.Cascade);

            return Ok();
        }

        // Owner/admin: lists every already-played match a cascade delete/edit on this match would
        // reopen, deepest-first, so the client can show the collateral before the user confirms.
        // Empty list = nothing downstream has been played (no cascade needed).
        [HttpPost("matchResult/cascadePreview")]
        public async Task<IActionResult> CascadeRevertPreview([FromBody] MatchResultDecisionRequest request)
        {
            var affected = await this.bracketService.GetCascadeRevertPreview(request.MatchId);

            return Ok(affected);
        }

        // Admin/owner-only: both sides no-showed an elimination match, so it is closed with no winner
        // and the opponent from the sibling matchup advances by walkover. Authorization is enforced
        // inside the service (CanManageTournamentAsync).
        [HttpPost("matchResult/doubleWalkover")]
        public async Task<IActionResult> ApplyDoubleWalkover([FromBody] MatchResultDecisionRequest request)
        {
            await this.bracketService.ApplyDoubleWalkover(request.MatchId);

            return Ok();
        }

        // Admin/owner-only bulk sibling: closes every fixture of ONE round that is still owed as a
        // double walkover and answers with the counts, so the organizer sees what actually happened
        // instead of a silent refresh. Authorization and eligibility live in the service.
        [HttpPost("matchResult/roundWalkover")]
        public async Task<IActionResult> ApplyRoundWalkover([FromBody] RoundWalkoverRequest request)
        {
            var result = await this.bracketService.ApplyRoundWalkover(request.StageId, request.RoundNumber);

            return Ok(result);
        }

        [HttpGet("{id}/details")]
        public async Task<IActionResult> GetDetails(Guid id)
        {
            var tournament = await this.Service.GetDetailsById(id);

            return Ok(tournament);
        }

        [HttpPost("{id}/closeRegistration")]
        public async Task<IActionResult> CloseRegistration([FromRoute] Guid id)
        {
            await this.Service.CloseRegistration(id);

            return Ok();
        }

        [HttpPost("{id}/openRegistration")]
        public async Task<IActionResult> OpenRegistration([FromRoute] Guid id)
        {
            await this.Service.OpenRegistration(id);

            return Ok();
        }

        [HttpPost("{id}/publish")]
        public async Task<IActionResult> Publish([FromRoute] Guid id)
        {
            await this.Service.Publish(id);

            return Ok();
        }

        [HttpGet("{id}/overview")]
        public async Task<IActionResult> GetOverview([FromRoute] Guid id)
        {
            var data = await this.Service.GetOverview(id);

            return Ok(data);
        }

        [HttpGet("{id}/overview/v2")]
        public async Task<IActionResult> GetOverviewV2([FromRoute] Guid id)
        {
            var data = await this.Service.GetOverviewV2(id);

            return Ok(data);
        }

        // v3 folds the CHECK_REGISTRATION round-trip into the overview payload — mobile no
        // longer needs a second call to render the Join / Registered button state.
        [HttpGet("{id}/overview/v3")]
        public async Task<IActionResult> GetOverviewV3([FromRoute] Guid id)
        {
            var data = await this.Service.GetOverviewV3(id);

            return Ok(data);
        }

        [HttpGet("{id}/user/{userId}/registred")]
        public async Task<IActionResult> CheckIsUserRegistred(Guid id, Guid userId)
        {
            var isUserAlreadyRegistred = await this.Service.CheckIsUserRegistred(id, userId);

            return Ok(isUserAlreadyRegistred);
        }

        [HttpPut("{id}/roundSchedule")]
        public async Task<IActionResult> SetRoundDeadline([FromRoute] Guid id, [FromBody] SetRoundDeadlineRequest request)
        {
            await this.Service.SetRoundDeadline(id, request.RoundNumber, request.Deadline, request.RoundStart, request.StageId, request.ClearRoundStart, request.ClearDeadline);

            return Ok();
        }

        /// <summary>
        /// Per-round series format, the format sibling of <c>PUT {id}/roundSchedule</c>. Matches in
        /// the round that already carry a reported result keep the format they were played under;
        /// the response says how many were skipped for that reason.
        /// </summary>
        [HttpPut("{id}/roundBestOf")]
        public async Task<IActionResult> SetRoundBestOf([FromRoute] Guid id, [FromBody] SetRoundBestOfRequest request)
        {
            var result = await this.Service.SetRoundBestOf(
                id, request.RoundNumber, request.BestOf, request.TiebreakBestOf, request.StageId, request.ClearBestOf);

            return Ok(result);
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelTournament([FromRoute] Guid id)
        {
            await this.Service.CancelTournament(id);

            return Ok();
        }

        [HttpDelete("{id}/hardDelete")]
        public async Task<IActionResult> HardDeleteTournament([FromRoute] Guid id)
        {
            await this.Service.HardDeleteTournament(id);

            return Ok();
        }

        [HttpGet("{tournamentId}/teams")]
        public async Task<IActionResult> GetTeamsByTournament(Guid tournamentId)
        {
            var teams = await this.tournamentTeamService.GetTeamsByTournament(tournamentId);

            return Ok(teams);
        }

        [HttpGet("{tournamentId}/teams/me")]
        public async Task<IActionResult> GetTeamsByTournamentForUser(Guid tournamentId)
        {
            var teams = await this.tournamentTeamService.GetTeamsByTournamentForUser(tournamentId);

            return Ok(teams);
        }

        [HttpGet("{tournamentId}/finalTeams")]
        public async Task<IActionResult> GetFinalTeamsByTournament(Guid tournamentId)
        {
            var teams = await this.tournamentTeamService.GetFinalTeamsByTournament(tournamentId);

            return Ok(teams);
        }

        [HttpGet("{tournamentId}/myTeam")]
        public async Task<IActionResult> GetTeamByTournament(Guid tournamentId)
        {
            var team = await this.tournamentTeamService.GetTeamByTournament(tournamentId);
            return Ok(team);
        }

        [AllowAnonymous]
        [HttpGet("{id}/export/pdf")]
        public async Task<IActionResult> ExportBracketPdf(Guid id, [FromQuery] bool includeSchedule = false)
        {
            // Additive optional flag — a request without it is byte-identical to the legacy call
            // (standings only). includeSchedule=true adds round-by-round fixture/result pages for
            // group-stage and league tournaments.
            var (pdf, name) = await this.tournamentExportService.GenerateBracketPdfAsync(id, includeSchedule);
            var safeName = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            var suffix = includeSchedule ? "-schedule" : "";
            return File(pdf, "application/pdf", $"bracket-{safeName}{suffix}.pdf");
        }

        // Machine-readable export for scripted consumers (a site pulling the tournament's rankings
        // and results on a schedule). CSV holds one table, so the two things worth ingesting are
        // separate datasets rather than one file with mixed schemas:
        //   ?dataset=standings — group / league / Swiss tables, a row per participant per group
        //   ?dataset=matches   — every fixture in the tournament, a row per match
        // Anonymous like the PDF export so an unattended fetch needs no token.
        [AllowAnonymous]
        [HttpGet("{id}/export/csv")]
        public async Task<IActionResult> ExportTournamentCsv(Guid id, [FromQuery] string dataset = "standings")
        {
            if (!Enum.TryParse<TournamentCsvDataset>(dataset, ignoreCase: true, out var parsedDataset)
                || !Enum.IsDefined(parsedDataset))
                throw new BusinessRuleException(this.localizationService["BusinessRule.InvalidExportDataset"]);

            var (csv, name) = await this.tournamentCsvExportService.GenerateCsvAsync(id, parsedDataset);
            var safeName = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            var kind = parsedDataset == TournamentCsvDataset.Matches ? "matches" : "standings";
            return File(csv, "text/csv; charset=utf-8", $"{kind}-{safeName}.csv");
        }
    }
}