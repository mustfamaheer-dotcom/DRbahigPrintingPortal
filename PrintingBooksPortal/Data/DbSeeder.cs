using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        try
        {
            if (!await roleManager.RoleExistsAsync("Admin"))
                await roleManager.CreateAsync(new IdentityRole("Admin"));
        }
        catch { }

        try
        {
            if (!await roleManager.RoleExistsAsync("Shop"))
                await roleManager.CreateAsync(new IdentityRole("Shop"));
        }
        catch { }

        try
        {
            if (await userManager.FindByEmailAsync("admin@printingbooks.com") == null)
            {
                var admin = new ApplicationUser
                {
                    UserName = "admin@printingbooks.com",
                    Email = "admin@printingbooks.com",
                    FullName = "System Administrator",
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(admin, "Admin@123");
                if (result.Succeeded)
                    await userManager.AddToRoleAsync(admin, "Admin");
            }
        }
        catch { }

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
            }
        }
        catch { }
    }
}
