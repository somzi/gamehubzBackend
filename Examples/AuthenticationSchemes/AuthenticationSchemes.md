Use multiple authentication schemes
===================================

Some apps may need to support multiple types of authentication.

For example, your app might authenticate users from Google
Identity and from a users database. In this case, the app should accept
a JWT bearer token from several issuers.*Add all authentication
schemes you'd like to accept* For example, the following code adds two
JWT bearer authentication schemes with different issuers:

```csharp
services.AddAuthentication(options =>
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
                            context.Response.Headers.Add("Token-Expired", "true");
                        }

                        return Task.CompletedTask;
                    },
                };
            }).AddJwtBearer("GoogleJwtValidation", o =>
            {
                o.IncludeErrorDetails = true;
                o.SecurityTokenValidators.Add(new GoogleAuthenticationScheme(builder.Configuration["GoogleToken:ClientId"]));
            });
```

Only one JWT bearer authentication is registered with the default
authentication scheme JwtBearerDefaults.AuthenticationScheme. Additional
authentication has to be registered with a unique authentication scheme.

Update the default authorization policy to accept both authentication
```csharp
            services.AddAuthorization(options =>
            {
                string[] authenticationSchemes = { JwtBearerDefaults.AuthenticationScheme, "GoogleJwtValidation" };
                var defaultAuthorizationPolicyBuilder = new AuthorizationPolicyBuilder(authenticationSchemes);
                defaultAuthorizationPolicyBuilder = defaultAuthorizationPolicyBuilder.RequireAuthenticatedUser();
                options.DefaultPolicy = defaultAuthorizationPolicyBuilder.Build();
            });
```

As the default authorization policy is overridden, it's possible to use
the \[Authorize\] attribute in controllers. The controller then accepts
requests with JWT issued by the first or second issuer.
