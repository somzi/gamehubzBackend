using GameHubz.DataModels.Models;

namespace GameHubz.DataModels.Tokens
{
	public class TokenResponse
	{
		public TokenResponse(bool isSuccessful, bool? isUserVerified, params string[] messages)
			: this(null, null, isSuccessful, null, isUserVerified, messages)

		{
		}

		public TokenResponse(bool isVerified)
			: this(null, null, isSuccessful: true, user: null, isVerified, messages: Array.Empty<string>())

		{
		}

		public TokenResponse(AccessToken accessToken, string refreshToken)
		: this(accessToken, refreshToken, isSuccessful: true, user: null, null, messages: Array.Empty<string>())

		{
		}

		public TokenResponse(AccessToken accessToken, string refreshToken, UserDto user, bool? isUserVerified)
			: this(accessToken, refreshToken, true, user, isUserVerified, messages: Array.Empty<string>())
		{
		}

		public TokenResponse(
			AccessToken? accessToken,
			string? refreshToken,
			bool isSuccessful,
			UserDto? user,
			bool? isUserVerified,
			params string[] messages
			)
		{
			this.AccessToken = accessToken;
			this.RefreshToken = refreshToken;
			this.User = user;
			this.IsSuccessful = isSuccessful;
			this.Messages = messages == null ? new List<string>() : messages.ToList();
			this.IsUserVerified = isUserVerified;
		}

		public bool IsSuccessful { get; }

		public List<string> Messages { get; }

		public AccessToken? AccessToken { get; }

		public UserDto? User { get; set; }

		public string? RefreshToken { get; }

		public bool? IsUserVerified { get; }
	}
}
