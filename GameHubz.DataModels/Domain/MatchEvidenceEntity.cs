using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class MatchEvidenceEntity : BaseEntity
    {
        /// <summary>Delivery URL. This is the only field the clients ever see.</summary>
        public string? Url { get; set; }

        /// <summary>
        /// Provider-native handle for the asset — Cloudinary's public_id, an object key on R2.
        /// Deleting needs this, not the URL: a Cloudinary URL carries folder, version and
        /// extension around the id, so parsing it back at delete time is guesswork. Nullable
        /// because rows written before migration 79 only ever stored the URL.
        /// </summary>
        public string? StorageKey { get; set; }

        public StorageProviderType Provider { get; set; }

        public EvidenceMediaType MediaType { get; set; }

        public Guid? MatchId { get; set; }

        public MatchEntity? Match { get; set; }
    }
}
