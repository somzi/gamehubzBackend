using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner;
using Template.Common.Interfaces;
using Template.Logic.Crypto;

namespace Template.DataMigrations.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            ConfigureMigrator(builder);

            builder.Services.AddTransient<IPasswordHasher, Pbkdf2Hasher>();

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }

        private static void ConfigureMigrator(
            WebApplicationBuilder builder)
        {
            builder.Services.AddFluentMigratorCore().ConfigureRunner(runnerBuilder =>
            {
                runnerBuilder
                    .AddSqlServer()
                    .WithGlobalConnectionString(builder.Configuration.GetConnectionString("DatabaseConnection"))
                    .ScanIn(typeof(Migration_00001_Scheme_Initial).Assembly)
                    .For.Migrations();
            }).Configure<RunnerOptions>(o =>
            {
                var tags = builder.Configuration.GetValue<string>("MigrationTag")!.Split(",");
                o.Tags = tags;
            });
        }
    }
}