namespace GameHubz.Logic.Utility
{
    /// <summary>
    /// Delivery-side resizing for the images we keep on Cloudinary.
    ///
    /// An avatar is stored at up to 800px and every byte of it that leaves the CDN is billed as
    /// bandwidth. Wherever the server hands an avatar to something that draws it small — a Discord
    /// card, a share page, a link preview — it should hand over a URL sized for that box instead of
    /// the original. Cloudinary generates the smaller version once and serves it from cache after.
    ///
    /// The mobile app does the same thing for everything it draws (src/lib/image.ts). The
    /// transformation string is deliberately identical to the app's, so a size both sides ask for
    /// is one derived asset, not two.
    /// </summary>
    public static class CloudinaryUrl
    {
        private const string UploadMarker = "/image/upload/";

        /// <summary>
        /// The same image, no wider than <paramref name="width"/> pixels (never upscaled).
        /// Anything that is not one of our Cloudinary image URLs comes back unchanged.
        /// </summary>
        /// <param name="autoFormat">
        /// Lets Cloudinary pick WebP/AVIF from the request's Accept header. Only for a URL a real
        /// browser will load: a link-preview crawler or a server-side decoder may be handed a format
        /// it cannot read, so those ask for the stored format instead.
        /// </param>
        public static string? Resized(string? url, int width, bool autoFormat = false)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (!url.Contains("cloudinary.com", StringComparison.OrdinalIgnoreCase)) return url;

            int at = url.IndexOf(UploadMarker, StringComparison.Ordinal);
            if (at < 0) return url;

            string transformation = autoFormat
                ? $"w_{width},c_limit,q_auto,f_auto"
                : $"w_{width},c_limit,q_auto";

            return url.Insert(at + UploadMarker.Length, transformation + "/");
        }
    }
}
