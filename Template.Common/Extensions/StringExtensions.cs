namespace Template.Common.Extensions
{
    public static class StringExtensions
    {
        public static Guid ToGuid(this string value)
        {
            return Guid.Parse(value);
        }

        public static string GetValueThrowIfNullOrEmpty(this string? value, string? errorMessage = null)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new Exception(errorMessage ?? "");
            }

            return value!;
        }
    }
}