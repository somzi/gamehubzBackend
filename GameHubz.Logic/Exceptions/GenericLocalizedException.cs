namespace GameHubz.Logic.Exceptions
{
    public class GenericLocalizedException : BaseException
    {
        public GenericLocalizedException(ILocalizationService localizationService, string translactionKey)
            : base(localizationService[translactionKey])
        {
        }
    }
}
