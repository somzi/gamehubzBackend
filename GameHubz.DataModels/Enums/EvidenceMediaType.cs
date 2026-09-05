namespace GameHubz.DataModels.Enums
{
    /// <summary>
    /// What kind of file an evidence row points at. Drives two things that differ sharply between
    /// the two: how the asset is uploaded (images get a server-side resize, videos are stored
    /// exactly as the phone compressed them) and how long it is kept (video is the storage cost
    /// driver, so it is purged days after a tournament ends; images are cheap enough to keep).
    /// </summary>
    public enum EvidenceMediaType
    {
        Image = 0,
        Video = 1
    }
}
