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
            DateTime endedBefore,
            DateTime createdBefore,
            int take,
            CancellationToken cancellationToken = default)
        {
            return this.BaseDbSet()
                .Where(x => x.MediaType == EvidenceMediaType.Video)
                .Where(x =>
                    // Normal path: the tournament is over and the grace window has passed.
                    //
                    // The clip must have cleared that window too, not just the tournament. Evidence
                    // attached days AFTER a tournament ended is being attached because someone is
                    // disputing the outcome — testing only the tournament date would delete it on
                    // the next sweep, minutes after it was uploaded.
                    (x.Match!.Tournament!.EndedOn != null
                        && x.Match.Tournament.EndedOn < endedBefore
                        && x.CreatedOn < endedBefore)
                    // Backstop: nothing ever ended, but the clip is old enough that it cannot be
                    // about a live match any more.
                    || x.CreatedOn < createdBefore)
                .OrderBy(x => x.CreatedOn)
                .Take(take)
                .ToListAsync(cancellationToken);
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
