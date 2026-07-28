using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ILogger<Program> logger)
    {
        string[] roles = ["Admin", "Teacher", "BookshopManager"];

        foreach (var role in roles)
        {
            try
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    var result = await roleManager.CreateAsync(new IdentityRole(role));
                    if (result.Succeeded)
                        logger.LogInformation("Role '{Role}' created.", role);
                    else
                        logger.LogWarning("Failed to create role '{Role}': {Errors}", role, string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to seed role '{Role}'.", role);
            }
        }

        try
        {
            if (await userManager.FindByEmailAsync("admin@printingbooks.com") == null)
            {
                var admin = new ApplicationUser
                {
                    UserName = "admin@printingbooks.com",
                    Email = "admin@printingbooks.com",
                    FullName = "System Administrator",
                    Role = UserRole.Admin,
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(admin, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Admin");
                    logger.LogInformation("Default Admin user created successfully.");
                }
                else
                {
                    logger.LogError("Failed to create Admin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed Admin user.");
        }

        try
        {
            if (!await db.EducationalBoards.AnyAsync())
            {
                db.EducationalBoards.AddRange(
                    new EducationalBoard { Name = "Cambridge IGCSE", Description = "Cambridge International General Certificate of Secondary Education" },
                    new EducationalBoard { Name = "Edexcel International", Description = "Pearson Edexcel International Curriculum" },
                    new EducationalBoard { Name = "IB Diploma", Description = "International Baccalaureate Diploma Programme" },
                    new EducationalBoard { Name = "National Curriculum", Description = "Local National Educational Board" }
                );
                await db.SaveChangesAsync();
                logger.LogInformation("Educational boards seeded successfully.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed Educational boards.");
        }
    }
}
