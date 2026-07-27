using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PrintingBooksPortal.Data;

/// <summary>
/// Used by 'dotnet ef' CLI commands to create the DbContext for migrations.
/// Connection string can be passed via: dotnet ef ... -- --connection "Server=...;"
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Server=localhost;Database=PrintingBooksPortal;Trusted_Connection=True;TrustServerCertificate=True;";

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--connection" && i + 1 < args.Length)
            {
                connectionString = args[i + 1];
                break;
            }
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
