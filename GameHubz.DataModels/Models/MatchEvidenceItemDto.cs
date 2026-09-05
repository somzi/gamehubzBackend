using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// One piece of evidence, with enough information for the client to decide how to render it.
    ///
    /// The flat Evidences list of URLs is kept alongside this because shipped app versions read it
    /// and would show nothing if it disappeared. New clients read EvidenceItems and fall back to
    /// the flat list, treating those entries as images (which is what every row was until video
    /// existed). The media type is carried explicitly rather than sniffed out of the URL: the
    /// resource marker in a Cloudinary path is a Cloudinary detail, and the storage layer exists
    /// precisely so that path can change.
    /// </summary>
    public class MatchEvidenceItemDto
    {
        public string Url { get; set; } = string.Empty;

        public EvidenceMediaType MediaType { get; set; }
    }
}
