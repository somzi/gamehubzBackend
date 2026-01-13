using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using GameHubz.Api.AuthenticationSchemes;
using GameHubz.DataModels.Config;

namespace GameHubz.Api.Startup
{
    internal class AuthenticationStartup
    {
        private const string GoogleJwtValidationSchemeName = "GoogleJwtValidation";

        internal static void ConfigureAuthentication(WebApplicationBuilder builder, IServiceCollection services)
        {
            IConfigurationSection authSettings = builder.Configuration.GetSection(nameof(AuthSettings));
            services.Configure<AuthSettings>(authSettings);

            var signingKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(authSettings[nameof(AuthSettings.SecretKey)]!));

            IConfigurationSection jwtAppSettingOptions = builder.Configuration.GetSection(nameof(AccessTokenOptions));

            services.Configure<AccessTokenOptions>(options =>
            {
                options.Issuer = jwtAppSettingOptions[nameof(AccessTokenOptions.Issuer)];
                options.Audience = jwtAppSettingOptions[nameof(AccessTokenOptions.Audience)];
                options.ValidFor = TimeSpan.FromSeconds(int.Parse(jwtAppSettingOptions[nameof(AccessTokenOptions.ValidFor)]!, CultureInfo.InvariantCulture));
                options.SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
            });

            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtAppSettingOptions[nameof(AccessTokenOptions.Issuer)],

                ValidateAudience = true,
                ValidAudience = jwtAppSettingOptions[nameof(AccessTokenOptions.Audience)],

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,

                RequireExpirationTime = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };

            AuthenticationBuilder authenticationBuilder = services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(configureOptions =>
            {
                configureOptions.ClaimsIssuer = jwtAppSettingOptions[nameof(AccessTokenOptions.Issuer)];
                configureOptions.TokenValidationParameters = tokenValidationParameters;
                configureOptions.SaveToken = true;

                configureOptions.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                        {
                            context.Response.Headers.Append("Token-Expired", "true");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

            bool isAzureLoginEnabled = builder.Configuration.GetValue<bool>("IsAzureLoginEnabled");

            if (isAzureLoginEnabled)
            {
                services.AddAuthentication(Consts.AzurewJwtValidationSchemeName)
                    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"), Consts.AzurewJwtValidationSchemeName)
                    .EnableTokenAcquisitionToCallDownstreamApi()
                    .AddMicrosoftGraph(builder.Configuration.GetSection("MicrosoftGraph"))
                    .AddInMemoryTokenCaches();
            }

            bool isGoogleEnabled = builder.Configuration.GetValue<bool>("Google:IsEnabled");

            if (isGoogleEnabled)
            {
                authenticationBuilder.AddJwtBearer(GoogleJwtValidationSchemeName, o =>
                {
                    o.IncludeErrorDetails = true;

                    // TODO:
                    /*
                     Severity	Code	Description	Project	File	Line	Suppression State	Details
                    Warning (active)	CS0618	'JwtBearerOptions.SecurityTokenValidators' is obsolete:
                    'SecurityTokenValidators is no longer used by default.
                    Use TokenHandlers instead.
                    To continue using SecurityTokenValidators, set UseSecurityTokenValidators to true.
                    See https://aka.ms/aspnetcore8/security-token-changes'
                     */

                    o.UseSecurityTokenValidators = true;
#pragma warning disable CS0618 // Type or member is obsolete
                    o.SecurityTokenValidators.Add(new GoogleAuthenticationScheme(builder.Configuration.GetStringThrowIfNull("Google:ClientId")));
#pragma warning restore CS0618 // Type or member is obsolete
                });
            }

            services.AddAuthorization(options =>
            {
                var authSchemes = new List<string>() { JwtBearerDefaults.AuthenticationScheme };

                if (isGoogleEnabled)
                {
                    authSchemes.Add(GoogleJwtValidationSchemeName);
                }

                if (isAzureLoginEnabled)
                {
                    authSchemes.Add(Consts.AzurewJwtValidationSchemeName);
                }

                var defaultAuthorizationPolicyBuilder = new AuthorizationPolicyBuilder(authSchemes.ToArray())
                                                            .RequireAuthenticatedUser();

                options.DefaultPolicy = defaultAuthorizationPolicyBuilder.Build();

                // Uncomment in case we need custom policy authorization.
                //options.AddPolicy("ApiUser", policy => policy.RequireClaim(JwtClaimIdentifiers.Rol, JwtClaims.ApiAccess));
            });
        }
    }
}
