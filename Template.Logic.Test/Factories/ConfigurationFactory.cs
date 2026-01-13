using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text;

namespace Template.Logic.Test.Factories
{
    public class ConfigurationFactory
    {
        public IConfiguration CreateConfigurationFromLocalJson()
        {
            var json = File.ReadAllText("appsettings.json");

            IConfiguration configuration
                = new ConfigurationBuilder()
                    .AddJsonStream(
                        new MemoryStream(Encoding.ASCII.GetBytes(json)))
                    .Build();

            return configuration;
        }
    }
}