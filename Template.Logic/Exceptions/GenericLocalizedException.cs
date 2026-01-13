namespace Template.Logic.Exceptions
{
    public class GenericLocalizedException : BaseException
    {
        public GenericLocalizedException(ILocalizationService localizationService, string translactionKey)
            : base(localizationService[translactionKey])
        {
        }
    }
}