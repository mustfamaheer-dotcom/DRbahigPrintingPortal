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
    private readonly ILogger<LoginController> _logger;

    public LoginController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<LoginController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpPost("login")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Login([FromForm] string email, [FromForm] string password, [FromForm] bool rememberMe)
    {
        try
        {
            _logger.LogInformation("Login attempt for {Email}", email);

            var result = await _signInManager.PasswordSignInAsync(email, password, rememberMe, lockoutOnFailure: false);
            if (result.Succeeded)
            {
                _logger.LogInformation("Login succeeded for {Email}", email);
                var user = await _userManager.FindByEmailAsync(email);
                if (user != null)
                {
                    if (await _userManager.IsInRoleAsync(user, "Admin"))
                        return Redirect("/admin/dashboard");
                    if (await _userManager.IsInRoleAsync(user, "Teacher"))
                        return Redirect("/teacher/dashboard");
                    if (await _userManager.IsInRoleAsync(user, "BookshopManager"))
                        return Redirect("/");
                }
                return Redirect("/");
            }

            _logger.LogWarning("Login failed for {Email}: {Result}", email, result);
            return Redirect("/login?error=" + Uri.EscapeDataString("Invalid email or password"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error for {Email}: {Message}", email, ex.Message);
            return Redirect("/login?error=" + Uri.EscapeDataString("An unexpected error occurred. Please try again."));
        }
    }
}
