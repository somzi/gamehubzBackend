namespace GameHubz.DataModels.Enums
{
    /// <summary>
    /// Which backend physically holds an uploaded asset. Everything runs on Cloudinary today; the
    /// column exists so the cleanup sweep can route a delete to the right provider once video
    /// moves to cheaper object storage, instead of that becoming a data migration.
    /// </summary>
    public enum StorageProviderType
    {
        Cloudinary = 0,
        CloudflareR2 = 1
    }
}
