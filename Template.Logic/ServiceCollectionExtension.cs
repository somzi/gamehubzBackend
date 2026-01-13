using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.DataModels.Config;
using Template.Logic.Crypto;
using Template.Logic.Queuing.Consumers.LocalQueueConsumers;
using Template.Logic.Queuing.Consumers.RabbitMqConsumers;
using Template.Logic.Queuing.Queues;
using Template.Logic.Queuing.Services.LocalQueueServices;
using Template.Logic.Queuing.Services.RabbitMqServices;
using Template.Logic.Services;
using Template.Logic.Tokens;
using Template.Logic.Validators;

namespace Template.Logic
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

            // DO NOT DELETE - Generated Service Tag

            ConfigureValidators(services);

            return services;
        }

        public static void ConfigureValidators(IServiceCollection services)
        {
            services.AddTransient<IValidator<UserEntity>, UserValidator>();
            services.AddTransient<IValidator<AssetEntity>, AssetValidator>();
            services.AddTransient<IValidator<EmailQueueEntity>, EmailQueueValidator>();
            services.AddTransient<IValidator<LoginRequestDto>, LoginRequestValidator>();

            //***********************************************
            //********** GENERATED **************************
            //***********************************************

            // DO NOT DELETE - Generated Validator Tag
        }
    }
}