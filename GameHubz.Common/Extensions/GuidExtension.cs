namespace GameHubz.Common.Extensions
{
    public static class GuidExtension
    {
        public static bool IsEmpty(this Guid guid)
        {
            return guid == Guid.Empty;
        }

        public static bool IsNullOrEmpty(this Guid? guid)
        {
            return guid == null || guid == Guid.Empty;
        }

        public static void ValidateEmptyAndThrow(this Guid guid, string argName)
        {
            if (guid.IsEmpty())
            {
                throw new ArgumentNullException(argName);
            }
        }

        public static void ValidateNullOrEmptyAndThrow(this Guid? guid, string argName)
        {
            if (guid.IsNullOrEmpty())
            {
                throw new ArgumentNullException(argName);
            }
        }
    }
}
