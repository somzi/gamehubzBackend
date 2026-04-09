using GameHubz.Api.BackgroundTasks;
using GameHubz.Api.Middleware;
using GameHubz.Api.Startup;
using GameHubz.Common.Interfaces;
using GameHubz.Data.Context;
using GameHubz.Data.UnitOfWork;
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
            builder.Services.AddControllers();

            builder.Configuration.AddEnvironmentVariables();

            builder.Services.AddEndpointsApiExplorer();

            builder.Services.AddHttpContextAccessor();

            builder.Services.AddHostedService<SendEmailTask>();

            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = builder.Configuration.GetConnectionString("Redis");
                options.InstanceName = "GameHubz_";
            });

            builder.Services.AddScoped<ICacheService, RedisCacheService>();

            IConfigurationSection rabbitMqSettings = builder.Configuration.GetSection(nameof(RabbitMq));
            builder.Services.Configure<RabbitMq>(rabbitMqSettings);
            builder.Services.AddSignalR();

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