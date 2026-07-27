using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Controllers;

[ApiController]
[Route("api")]
public class LoginController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public LoginController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpPost("login")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Login([FromForm] string email, [FromForm] string password, [FromForm] bool rememberMe)
    {
        var result = await _signInManager.PasswordSignInAsync(email, password, rememberMe, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null)
            {
                if (await _userManager.IsInRoleAsync(user, "Admin"))
                    return Redirect("/admin/dashboard");
                if (await _userManager.IsInRoleAsync(user, "Shop"))
                    return Redirect("/shop/mybooks");
            }
            return Redirect("/");
        }
        return Redirect("/login?error=" + Uri.EscapeDataString("Invalid email or password"));
    }
}
