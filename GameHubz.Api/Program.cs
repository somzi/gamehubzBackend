using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NLog.Web;
using GameHubz.Api.BackgroundTasks;
using GameHubz.Api.Middleware;
using GameHubz.Api.Startup;
using GameHubz.Common.Interfaces;
using GameHubz.Data.Context;
using GameHubz.Data.UnitOfWork;
using GameHubz.DataMigrations;
using GameHubz.DataModels.Config.RabbitMqConfig;
using GameHubz.Localization;
using GameHubz.Logic;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Mappings;

namespace GameHubz.Api
{
    public class Program
    {
        public static void Main(params string[] args)
        {
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

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();

            builder.Services.AddHttpContextAccessor();

            builder.Services.AddHostedService<SendEmailTask>();

            IConfigurationSection rabbitMqSettings = builder.Configuration.GetSection(nameof(RabbitMq));
            builder.Services.Configure<RabbitMq>(rabbitMqSettings);

            WebApplication app = builder.Build();

            RabbitMqStartup.ConfigureRabbitMqConsumers(app.Services);

            ConfigurePipeline(app, builder.Configuration);

            app.Run();
        }

        private static void ConfigureServices(
            IServiceCollection services)
        {
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddTransient<IUserContextReader, UserContextReader>();
            services.AddScoped<IUnitOfWorkFactory, UnitOfWorkFactory>();
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
                options => options.UseSqlServer(configuration.GetConnectionString("DatabaseConnection")!),
                ServiceLifetime.Transient,
                ServiceLifetime.Transient);
        }

#pragma warning disable IDE0060 // Remove unused parameter

        private static void ConfigurePipeline(WebApplication app, ConfigurationManager configurationManager)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseMiddleware<ExceptionHandlingMiddlware>();

            app.UseCors(x => x.AllowAnyMethod().WithOrigins("*").AllowAnyHeader());

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "GameHubz API");

                // TODO: refactor
                if (configurationManager.GetValue<bool>("IsAzureLoginEnabled"))
                {
                    var secret = configurationManager.GetValue<string>("AzureAd:ClientSecret");
                    var clientId = configurationManager.GetValue<string>("AzureAd:ClientId");

                    c.OAuthClientId(clientId);
                    c.OAuthClientSecret(secret);
                }
            });

            //app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
        }
    }
}