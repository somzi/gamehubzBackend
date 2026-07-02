using GameHubz.Api.BackgroundTasks;
using GameHubz.Api.Json;
using GameHubz.Api.Middleware;
using GameHubz.Api.Startup;
using GameHubz.Common.Interfaces;
using GameHubz.Data.Context;
using GameHubz.Data.UnitOfWork;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Config.RabbitMqConfig;
using GameHubz.Localization;
using GameHubz.Logic;
using GameHubz.Logic.Fonts;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Mappings;
using GameHubz.Logic.Services;
using GameHubz.Logic.SignalR;
using Microsoft.EntityFrameworkCore;
using NLog.Web;

namespace GameHubz.Api
{
    public class Program
    {
        public static void Main(params string[] args)
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            FontRegistration.RegisterEmbeddedFonts();

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.WebHost.UseNLog();

            builder.Services.AddLogicServices(builder.Configuration);

            ConfigureAutomapper(builder.Services);
            AuthenticationStartup.ConfigureAuthentication(builder, builder.Services);

            ConfigureServices(builder.Services);
            SwaggerStartup.ConfigureSwagger(builder);
            ConfigureDataContext(builder);

            // Add services to the container.
            builder.Services.AddControllers().AddJsonOptions(opts =>
            {
                // Always emit DateTime values with an explicit UTC marker ("Z") so the
                // client knows to parse as UTC and render in the user's local timezone.
                opts.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
                opts.JsonSerializerOptions.Converters.Add(new UtcNullableDateTimeJsonConverter());
            });

            builder.Configuration.AddEnvironmentVariables();

            builder.Services.AddEndpointsApiExplorer();

            builder.Services.AddHttpContextAccessor();

            builder.Services.AddHostedService<SendEmailTask>();

            // Approaching-deadline push reminders (registration closing / round deadline). The
            // hosted task runs the sweep on a fresh scope each tick; the runner does the work.
            builder.Services.AddScoped<DeadlineNotificationRunner>();
            builder.Services.AddHostedService<DeadlineNotificationTask>();

            builder.Services.AddHttpClient("ExpoPush", client =>
            {
                client.BaseAddress = new Uri("https://exp.host");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            // Discord webhook announcements. No BaseAddress — each hub stores its own absolute
            // webhook URL. Short timeout: the sends are fire-and-forget, nothing should linger.
            builder.Services.AddHttpClient("DiscordWebhook", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            // Share a single ConnectionMultiplexer between IDistributedCache (used for GET/SET)
            // and the pattern-based delete path in RedisCacheService (used for invalidating
            // every page of a paginated key family at once).
            var redisConnectionString = builder.Configuration.GetConnectionString("Redis")!;
            var sharedMultiplexer = StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString);
            builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sharedMultiplexer);

            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.ConnectionMultiplexerFactory = () => Task.FromResult<StackExchange.Redis.IConnectionMultiplexer>(sharedMultiplexer);
                options.InstanceName = "GameHubz_";
            });

            builder.Services.AddScoped<ICacheService, RedisCacheService>();

            IConfigurationSection rabbitMqSettings = builder.Configuration.GetSection(nameof(RabbitMq));
            builder.Services.Configure<RabbitMq>(rabbitMqSettings);

            IConfigurationSection shareLinksSettings = builder.Configuration.GetSection(nameof(ShareLinksConfig));
            builder.Services.Configure<ShareLinksConfig>(shareLinksSettings);
            builder.Services.AddSignalR().AddJsonProtocol(opts =>
            {
                // Same UTC marker treatment for messages pushed through SignalR hubs
                // (chat, DM, live updates) — otherwise client-side timestamps drift.
                opts.PayloadSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
                opts.PayloadSerializerOptions.Converters.Add(new UtcNullableDateTimeJsonConverter());
            });

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll",
                    policy =>
                    {
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    });
            });

            WebApplication app = builder.Build();

            RabbitMqStartup.ConfigureRabbitMqConsumers(app.Services);

            ConfigurePipeline(app, builder.Configuration);

            app.MapHub<MatchChatHub>("/hubs/chat");
            app.MapHub<DirectChatHub>("/hubs/dm");
            app.MapHub<UserHub>("/hubs/user");

            app.Run();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddTransient<IUserContextReader, UserContextReader>();
            services.AddScoped<IUnitOfWorkFactory, UnitOfWorkFactory>();
            services.AddScoped<CloudinaryStorageService>();
        }

        private static void ConfigureAutomapper(IServiceCollection services)
        {
            services.AddAutoMapper(
                (provider, mapperConfiguration) =>
                {
                    var localizationService = provider.GetRequiredService<ILocalizationService>();
                    var configuration = provider.GetRequiredService<IConfiguration>();
                    MapperRegistrator.Register(mapperConfiguration, localizationService, configuration);
                },
                new[] { typeof(Program).Assembly },
                ServiceLifetime.Singleton);
        }

        private static void ConfigureDataContext(WebApplicationBuilder builder)
        {
            ConfigurationManager configuration = builder.Configuration;
            builder.Services.AddDbContext<ApplicationContext>(
                options => options.UseNpgsql(configuration.GetConnectionString("DatabaseConnection")!),
                ServiceLifetime.Transient,
                ServiceLifetime.Transient);
        }

        private static void ConfigurePipeline(WebApplication app, ConfigurationManager configurationManager)
        {
            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseMiddleware<ExceptionHandlingMiddlware>();

            // PRAVILAN REDOSLED ZA CORS
            app.UseRouting();

            // Aktiviramo CORS polisu koju smo gore definisali
            app.UseCors("AllowAll");

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "GameHubz API");

                if (configurationManager.GetValue<bool>("IsAzureLoginEnabled"))
                {
                    var secret = configurationManager.GetValue<string>("AzureAd:ClientSecret");
                    var clientId = configurationManager.GetValue<string>("AzureAd:ClientId");

                    c.OAuthClientId(clientId);
                    c.OAuthClientSecret(secret);
                }
            });

            // app.UseHttpsRedirection(); // Isključeno za rad preko IP adrese

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();
        }
    }
}