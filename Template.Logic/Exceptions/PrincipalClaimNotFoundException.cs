namespace Template.Logic.Exceptions
{
    public class PrincipalClaimNotFoundException : BaseException
    {
        public PrincipalClaimNotFoundException(ILocalizationService localizationService)
            : base(localizationService, "Exception.PrincipalClaimNotFoundException")
        {
        }
    }
}