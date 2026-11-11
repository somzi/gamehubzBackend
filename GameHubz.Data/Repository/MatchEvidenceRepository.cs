using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class MatchEvidenceRepository : BaseRepository<ApplicationContext, MatchEvidenceEntity>, IMatchEvidenceRepository
    {
        public MatchEvidenceRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public Task<int> CountVideosForMatch(Guid matchId)
        {
            return this.BaseDbSet()
                .CountAsync(x => x.MatchId == matchId && x.MediaType == EvidenceMediaType.Video);
        }

        public Task<List<MatchEvidenceEntity>> GetExpiredVideos(
            DateTime matchSettledBefore,
            DateTime endedBefore,
            DateTime createdBefore,
            int take,
            CancellationToken cancellationToken = default)
        {
            return this.BaseDbSet()
                .Where(x => x.MediaType == EvidenceMediaType.Video)
                .Where(x =>
                    // Normal path: the clip's own match is settled and nobody has touched it since.
                    //
                    // A league runs for weeks, and keyed off the tournament alone every clip of it
                    // sat in storage until the final. A clip proves one result; once that result
                    // has stood for the window there is nothing left for it to settle.
                    //
                    // "Left alone" is the match's last write, so an edited or re-entered result
                    // restarts the clock. The clip has to have cleared the window too: one attached
                    // late is being attached because someone is disputing the result. An open
                    // admin-help request or a pending proposal means the result is still being
                    // argued about, and the clip is what the argument is settled with.
                    (x.CreatedOn < matchSettledBefore
                        && (x.Match!.Status == MatchStatus.Completed || x.Match.Status == MatchStatus.NoShow)
                        && (x.Match.ModifiedOn ?? x.Match.CreatedOn) < matchSettledBefore
                        && !x.Match.AdminHelpRequested
                        && x.Match.ProposedByUserId == null)
                    // The tournament is over and the grace window has passed.
                    //
                    // The clip must have cleared that window too, not just the tournament. Evidence
                    // attached days AFTER a tournament ended is being attached because someone is
                    // disputing the outcome — testing only the tournament date would delete it on
                    // the next sweep, minutes after it was uploaded.
                    || (x.Match!.Tournament!.EndedOn != null
                        && x.Match.Tournament.EndedOn < endedBefore
                        && x.CreatedOn < endedBefore)
                    // Backstop: nothing ever ended, but the clip is old enough that it cannot be
                    // about a live match any more.
                    || x.CreatedOn < createdBefore)
                .OrderBy(x => x.CreatedOn)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        public Task<List<MatchEvidenceEntity>> GetExpiredImages(
            DateTime endedBefore,
            DateTime createdBefore,
            int take,
            CancellationToken cancellationToken = default)
        {
            return this.BaseDbSet()
                .Where(x => x.MediaType == EvidenceMediaType.Image)
                .Where(x =>
                    // The tournament is over and the grace window has passed — for the tournament
                    // and for the screenshot itself, same reasoning as the clips above.
                    (x.Match!.Tournament!.EndedOn != null
                        && x.Match.Tournament.EndedOn < endedBefore
                        && x.CreatedOn < endedBefore)
                    // A tournament that runs longer than the window, or never formally ends.
                    || x.CreatedOn < createdBefore)
                .OrderBy(x => x.CreatedOn)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        public async Task<HashSet<Guid>> GetMatchIdsWithExpiredEvidenceByTournament(Guid tournamentId)
        {
            // IgnoreQueryFilters: a retired row is a soft-deleted one, which the filter hides.
            var ids = await this.ContextBase.Set<MatchEvidenceEntity>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(x => x.IsDeleted && x.MatchId != null && x.Match!.TournamentId == tournamentId)
                .Select(x => x.MatchId!.Value)
                .Distinct()
                .ToListAsync();

            return ids.ToHashSet();
        }

        public async Task<HashSet<Guid>> GetMatchIdsWithExpiredEvidence(IReadOnlyCollection<Guid> matchIds)
        {
            if (matchIds.Count == 0) return new HashSet<Guid>();

            var ids = await this.ContextBase.Set<MatchEvidenceEntity>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(x => x.IsDeleted && x.MatchId != null && matchIds.Contains(x.MatchId.Value))
                .Select(x => x.MatchId!.Value)
                .Distinct()
                .ToListAsync();

            return ids.ToHashSet();
        }

        public Task<List<MatchEvidenceEntity>> GetByTournament(Guid tournamentId, EvidenceMediaType mediaType)
        {
            return this.BaseDbSet()
                .Where(x => x.MediaType == mediaType && x.Match!.TournamentId == tournamentId)
                .ToListAsync();
        }

        public Task<bool> AnyForMatches(IReadOnlyCollection<Guid> matchIds, bool includeSoftDeleted)
        {
            if (matchIds.Count == 0) return Task.FromResult(false);

            var query = this.ContextBase.Set<MatchEvidenceEntity>().AsNoTracking();
            if (includeSoftDeleted) query = query.IgnoreQueryFilters();

            return query.AnyAsync(x => x.MatchId != null && matchIds.Contains(x.MatchId.Value));
        }
    }
}
