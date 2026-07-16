using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Ben.Data.Source
{
    public class BenDataContextDesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext(string[] args)
        {
            // When running dotnet ef from the solution root with --startup-project Ben.Web.WebApp,
            // the working directory is set to the startup project folder.
            var basePath = Directory.GetCurrentDirectory();

            // Fallback: navigate from Ben.Data.Source to Ben.Web.WebApp
            if (!File.Exists(Path.Combine(basePath, "appsettings.json")))
                basePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Ben.Web.WebApp");

            var config = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();

            var connectionString = config.GetConnectionString("BenDbConnectionString")
                ?? throw new InvalidOperationException("Connection string 'BenDbConnectionString' not found.");

            var optionsBuilder = new DbContextOptionsBuilder<BenDataContext>();
            optionsBuilder.UseSqlServer(connectionString);

            return new BenDataContext(optionsBuilder.Options);
        }
    }
}
