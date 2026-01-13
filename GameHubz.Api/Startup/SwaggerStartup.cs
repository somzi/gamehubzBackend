using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using GameHubz.Common.Extensions;

namespace GameHubz.Api.Middleware
{
    internal class SwaggerStartup
    {
        internal static void ConfigureSwagger(WebApplicationBuilder builder)
        {
            builder.Services.AddSwaggerGen(setup =>
            {
                ConfigureSwaggerSecurityJwt(setup);
                ConfigureSwaggerSecurityBasic(setup);

                if (builder.Configuration.GetValue<bool>("IsAzureLoginEnabled"))
                {
                    ConfigureSwaggerSecurityAzure(builder, setup);
                }
            });
        }

        private static void ConfigureSwaggerSecurityAzure(WebApplicationBuilder builder, SwaggerGenOptions setup)
        {
            string clientId = builder.Configuration.GetValueThrowIfNull<string>("AzureAd:ClientId");

            OpenApiSecurityScheme openApiScheme = new()
            {
                Scheme = Consts.AzurewJwtValidationSchemeName,
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    Implicit = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri("https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize"),
                        TokenUrl = new Uri("https://login.microsoftonline.com/organizations/oauth2/v2.0/token"),
                        Scopes = new Dictionary<string, string>
                            {
                                { $"api://{clientId}/access_as_user", "access_as_user" }
                            }
                    }
                }
            };

            setup.AddSecurityDefinition(Consts.AzurewJwtValidationSchemeName, openApiScheme);

            setup.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = Consts.AzurewJwtValidationSchemeName
                            },
                            Scheme = Consts.AzurewJwtValidationSchemeName,
                            Name = Consts.AzurewJwtValidationSchemeName,
                            In = ParameterLocation.Header
                        },
                        new List<string>()
                    }
                });
        }

        private static void ConfigureSwaggerSecurityJwt(SwaggerGenOptions setup)
        {
            var jwtSecurityScheme = new OpenApiSecurityScheme
            {
                Scheme = "bearer",
                BearerFormat = "JWT",
                Name = "JWT Authentication",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Description = "Put **_ONLY_** your JWT Bearer token on textbox below!",

                Reference = new OpenApiReference
                {
                    Id = JwtBearerDefaults.AuthenticationScheme,
                    Type = ReferenceType.SecurityScheme
                }
            };

            setup.AddSecurityDefinition(jwtSecurityScheme.Reference.Id, jwtSecurityScheme);

            setup.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                { jwtSecurityScheme, Array.Empty<string>() }
                });
        }

        private static void ConfigureSwaggerSecurityBasic(SwaggerGenOptions setup)
        {
            setup.AddSecurityDefinition(
               "Basic",
               new OpenApiSecurityScheme
               {
                   Description = "Authorization header using the Basic scheme.",
                   Type = SecuritySchemeType.Http,
                   Scheme = "basic",
               });

            setup.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Id = "Basic",
                                Type = ReferenceType.SecurityScheme,
                            },
                        },  new List<string>()
                    },
                });
        }
    }
}
