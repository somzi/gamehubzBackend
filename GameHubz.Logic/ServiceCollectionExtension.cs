using FluentValidation;
using GameHubz.DataModels.Config;
using GameHubz.Logic.Crypto;
using GameHubz.Logic.Queuing.Consumers.LocalQueueConsumers;
using GameHubz.Logic.Queuing.Consumers.RabbitMqConsumers;
using GameHubz.Logic.Queuing.Queues;
using GameHubz.Logic.Queuing.Services.LocalQueueServices;
using GameHubz.Logic.Queuing.Services.RabbitMqServices;
using GameHubz.Logic.Services;
using GameHubz.Logic.Tokens;
using GameHubz.Logic.Validators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GameHubz.Logic
{
    public static class ServiceCollectionExtension
    {
        public static IServiceCollection AddLogicServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddScoped<DateTimeProvider>();

            services.AddSingleton<IAccessTokenFactory, AccessTokenFactory>();
            services.AddSingleton<IAccessTokenHandler, AccessTokenHandler>();
            services.AddSingleton<IRefreshTokenFactory, RefreshTokenFactory>();
            services.AddSingleton<IRabbitMqConnectionService, RabbitMqConnectionService>();
            services.AddSingleton<ConsumerRegister>();

            services.AddTransient<IPasswordHasher, Pbkdf2Hasher>();
            services.AddTransient<IRabbitMqQueueService, RabbitMqQueueService>();
            services.AddTransient<IRabbitMqConfigService, RabbitMqConfigService>();
            services.AddTransient<LocalQueueEmailConsumer>();

            services.AddTransient<AccessTokenReader>();
            services.AddTransient<IFilterExpressionBuilder, FilterExpressionBuilder>();
            services.AddTransient<ISortStringBuilder, SortStringBuilder>();
            services.AddTransient<AssetService>();
            services.AddTransient<UserService>();

            services.AddTransient<ImageService>();
            services.AddTransient<AesCrypter>();
            services.AddTransient<LoggerService>();
            services.AddTransient<UserService>();
            services.AddTransient<HubService>();
            services.AddTransient<SearchService>();
            services.AddTransient<AuthService>();
            services.AddTransient<GoogleAuthService>();
            services.AddTransient<AnonymousUserContextReader>();
            services.AddTransient<PasswordManagementService>();
            services.AddTransient<LocalQueueEmailService>();
            services.AddTransient<EmailService>();

            services.AddTransient<ServiceFunctions>();
            services.AddTransient<AppAuthorizationService>();

            services.AddTransient<EmailQueue>();

            IConfigurationSection blobConfigConfiguration = configuration.GetSection(nameof(BlobConfig));
            services.Configure<BlobConfig>(blobConfigConfiguration);
            services.AddTransient<BlobService>();

            IConfigurationSection smtpOptionsConfiguration = configuration.GetSection(nameof(SmtpOptions));
            services.Configure<SmtpOptions>(smtpOptionsConfiguration);

            //***********************************************
            //********** GENERATED **************************
            //***********************************************

            services.AddTransient<UserHubService>();
            services.AddTransient<TournamentService>();
            services.AddTransient<TournamentRegistrationService>();
            services.AddTransient<MatchService>();
            services.AddTransient<UserSocialService>();

            // DO NOT DELETE - Generated Service Tag

            ConfigureValidators(services);

            return services;
        }

        public static void ConfigureValidators(IServiceCollection services)
        {
            services.AddTransient<IValidator<UserEntity>, UserValidator>();
            services.AddTransient<IValidator<AssetEntity>, AssetValidator>();
            services.AddTransient<IValidator<EmailQueueEntity>, EmailQueueValidator>();
            services.AddTransient<IValidator<HubEntity>, HubValidator>();
            services.AddTransient<IValidator<LoginRequestDto>, LoginRequestValidator>();

            //***********************************************
            //********** GENERATED **************************
            //***********************************************

            services.AddTransient<IValidator<UserHubEntity>, UserHubValidator>();
            services.AddTransient<IValidator<TournamentEntity>, TournamentValidator>();
            services.AddTransient<IValidator<TournamentRegistrationEntity>, TournamentRegistrationValidator>();
            services.AddTransient<IValidator<MatchEntity>, MatchValidator>();

            services.AddTransient<IValidator<UserSocialEntity>, UserSocialValidator>();

            // DO NOT DELETE - Generated Validator Tag
        }
    }
}