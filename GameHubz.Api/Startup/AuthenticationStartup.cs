using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using GameHubz.DataModels.Config;

namespace GameHubz.Api.Startup
{
    internal class AuthenticationStartup
    {
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

                    // WebSocket clients (SignalR hubs under /hubs) can't set an Authorization
                    // header, so they pass the JWT as the "access_token" query parameter.
                    // Only applied to hub paths; regular API requests keep using the header.
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
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

            services.AddAuthorization(options =>
            {
                var authSchemes = new List<string>() { JwtBearerDefaults.AuthenticationScheme };

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
