using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PrintingBooksPortal.Models;

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
        if (connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            optionsBuilder.UseSqlite(connectionString);
        else
            optionsBuilder.UseSqlServer(connectionString);

        // tenantContext null + multiTenancy disabled → query filters skipped at design time (§3.5)
        return new AppDbContext(optionsBuilder.Options, tenantContext: null, multiTenancy: new MultiTenancyOptions { Enabled = false });
    }
}
