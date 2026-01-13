namespace GameHubz.Logic.Exceptions
{
	public class UserIsAlreadyVerifiedException : BaseException
	{
		public UserIsAlreadyVerifiedException(
			ILocalizationService localizationService)
			: base(localizationService, "Exception.UserIsAlreadyVerified")
		{
		}
	}
}
