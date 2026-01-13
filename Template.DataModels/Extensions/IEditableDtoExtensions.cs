using Template.DataModels.Interfaces;

namespace Template.DataModels.Extensions
{
    public static class IEditableDtoExtensions
    {
        public static bool IsNew(this IEditableDto modifiedDto)
        {
            if (modifiedDto is null)
            {
                throw new ArgumentNullException(nameof(modifiedDto));
            }

            return !modifiedDto.Id.HasValue;
        }
    }
}