namespace GameHubz.Logic.Exceptions.Base
{
    public class BaseUnauthorizedException : BaseException
    {
        public BaseUnauthorizedException(
            ILocalizationService localizationService,
            string key,
            params string[] args)
            : base(localizationService, key, args)
        {
        }
    }
}
