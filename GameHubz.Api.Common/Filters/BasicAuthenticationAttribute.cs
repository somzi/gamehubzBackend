using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace GameHubz.Api.Common.Filters
{
    public sealed class BasicAuthenticationAttribute : ActionFilterAttribute
    {
        private readonly string configurationKey;

        public BasicAuthenticationAttribute(string configurationKey)
        {
            this.configurationKey = configurationKey;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            IConfiguration? configuration = context.HttpContext.RequestServices.GetService<IConfiguration>();

            if (configuration == null)
            {
#pragma warning disable CA2208 // Instantiate argument exceptions correctly
                throw new ArgumentNullException(nameof(configuration));
#pragma warning restore CA2208 // Instantiate argument exceptions correctly
            }

            var usernameConfig = configuration.GetValue<string>($"{this.configurationKey}:Username");
            var passwordConfig = configuration.GetValue<string>($"{this.configurationKey}:Password");

            var result = this.GetCredentialsFromHeader(context, configuration);

            if (!result.IsValid)
            {
                return;
            }

            if (result.Username == usernameConfig
                && result.Password == passwordConfig)
            {
                // success
                return;
            }

            this.SetUnauthorized(context, configuration);
            return;
        }

        private static bool IsValidateBase64Valid(string base64Value)
        {
            if (string.IsNullOrWhiteSpace(base64Value) ||
                base64Value.Length % 4 != 0)
            {
                return false;
            }

            try
            {
                Convert.FromBase64String(base64Value);
            }
            catch (FormatException)
            {
                return false;
            }

            return true;
        }

        private RequestParseResult GetCredentialsFromHeader(
                    ActionExecutingContext context,
            IConfiguration configuration)
        {
            var request = context.HttpContext.Request;
            StringValues auth = request.Headers["Authorization"];
            var startString = "Basic ";

            if (string.IsNullOrEmpty(auth) || !auth[0]!.StartsWith(startString, StringComparison.OrdinalIgnoreCase))
            {
                return GetInvalidResponse();
            }

            var base64HeaderValue = auth[0]![startString.Length..];

            if (!IsValidateBase64Valid(base64HeaderValue))
            {
                return GetInvalidResponse();
            }

            var cred = ASCIIEncoding.ASCII.GetString(Convert.FromBase64String(base64HeaderValue)).Split(':');

            if (cred.Length < 2)
            {
                return GetInvalidResponse();
            }

            return new RequestParseResult(
                         true,
                         cred[0],
                         cred[1]);

            RequestParseResult GetInvalidResponse()
            {
                this.SetUnauthorized(context, configuration);

                return new RequestParseResult(
                        false,
                        string.Empty,
                        string.Empty);
            }
        }

        private void SetUnauthorized(ActionExecutingContext filterContext, IConfiguration configuration)
        {
            var realm = "App";
            filterContext.HttpContext.Response.Headers["WWW-Authenticate"] = string.Format(CultureInfo.InvariantCulture, "Basic realm=\"{0}\"", realm);
            filterContext.Result = new UnauthorizedResult();
        }

        private class RequestParseResult
        {
            public RequestParseResult(
                bool isValid,
                string username,
                string password)
            {
                this.IsValid = isValid;
                this.Username = username;
                this.Password = password;
            }

            public bool IsValid { get; set; }
            public string Password { get; set; }
            public string Username { get; set; }
        }
    }
}
