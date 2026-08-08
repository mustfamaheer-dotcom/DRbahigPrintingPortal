using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PrintingBooksPortal.Services;

namespace PrintingBooksPortal.Controllers;

[ApiController]
[Route("api/sa")]
[Authorize(Roles = "SystemAdmin")]
[IgnoreAntiforgeryToken]   // JSON API called from Blazor circuits — no form antiforgery token is attached
public class SystemAdminController : ControllerBase
{
    private readonly SystemAdminService _sa;

    public SystemAdminController(SystemAdminService sa)
    {
        _sa = sa;
    }

    // ── Teachers (tenants) ──

    [HttpGet("teachers")]
    public async Task<IActionResult> ListTeachers()
    {
        var teachers = await _sa.ListTeachersAsync();
        return Ok(new { teachers, totalCount = teachers.Count });
    }

    [HttpPost("teachers")]
    public async Task<IActionResult> CreateTeacher([FromBody] CreateTeacherRequest request)
    {
        var (tenantId, error) = await _sa.CreateTeacherAsync(new CreateTeacherData
        {
            Name = request?.Name ?? string.Empty,
            OwnerName = request?.OwnerName,
            ContactEmail = request?.ContactEmail ?? string.Empty,
            Phone = request?.Phone,
            Password = request?.Password ?? string.Empty,
            MaxShops = request?.MaxShops,
            MaxBooks = request?.MaxBooks,
            Plan = request?.Plan
        });

        if (error == "A user with this email already exists.")
            return Conflict(new { error });

        if (tenantId == null)
            return BadRequest(new { error });

        return StatusCode(201, new
        {
            id = tenantId,
            name = request.Name.Trim(),
            contactEmail = request.ContactEmail.Trim(),
            userName = request.ContactEmail.Trim(),
            message = $"Teacher created. Account: {request.ContactEmail.Trim()}"
        });
    }

    [HttpPut("teachers/{id:int}")]
    public async Task<IActionResult> UpdateTeacher(int id, [FromBody] UpdateTeacherRequest request)
    {
        var (ok, error) = await _sa.UpdateTeacherAsync(id, new UpdateTeacherData
        {
            Name = request?.Name,
            OwnerName = request?.OwnerName,
            ContactEmail = request?.ContactEmail,
            Phone = request?.Phone,
            MaxShops = request?.MaxShops,
            MaxBooks = request?.MaxBooks,
            Plan = request?.Plan
        });

        if (!ok && error == "A user with this email already exists.")
            return Conflict(new { error });

        if (!ok)
            return NotFound(new { error });

        return Ok(new { id, message = "Teacher updated." });
    }

    [HttpPost("teachers/{id:int}/deactivate")]
    public async Task<IActionResult> DeactivateTeacher(int id)
    {
        var (ok, error) = await _sa.SetTeacherActiveAsync(id, false);
        if (!ok)
            return NotFound(new { error });

        return Ok(new { success = true });
    }

    [HttpPost("teachers/{id:int}/activate")]
    public async Task<IActionResult> ActivateTeacher(int id)
    {
        var (ok, error) = await _sa.SetTeacherActiveAsync(id, true);
        if (!ok)
            return NotFound(new { error });

        return Ok(new { success = true });
    }

    [HttpPost("teachers/{id:int}/reset-password")]
    public async Task<IActionResult> ResetTeacherPassword(int id, [FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.NewPassword))
            return BadRequest(new { error = "New password is required." });

        var (ok, error) = await _sa.ResetTeacherPasswordAsync(id, request.NewPassword);
        if (!ok && error == "Teacher account not found.")
            return NotFound(new { error });

        if (!ok)
            return BadRequest(new { error });

        return Ok(new { success = true, message = "Password updated." });
    }

    [HttpDelete("teachers/{id:int}")]
    public async Task<IActionResult> DeleteTeacher(int id)
    {
        var (ok, error) = await _sa.DeleteTeacherAsync(id);
        if (!ok)
            return Conflict(new { error });

        return Ok(new { success = true, deleted = true });
    }

    // ── Analytics ──

    [HttpGet("analytics/summary")]
    public async Task<IActionResult> GetAnalyticsSummary()
    {
        return Ok(await _sa.GetPlatformSummaryAsync());
    }

    // ── Tenant drill-down ──

    [HttpGet("tenants/{id:int}")]
    public async Task<IActionResult> GetTenantDetails(int id)
    {
        var details = await _sa.GetTenantDetailsAsync(id);
        if (details == null)
            return NotFound(new { error = "Tenant not found." });

        return Ok(new
        {
            tenant = details.tenant,
            shops = details.shops,
            books = details.books,
            printLogs = details.printLogs,
            apiKeys = details.apiKeys
        });
    }

    // ── API keys ──

    [HttpGet("tenants/{id:int}/apikeys")]
    public async Task<IActionResult> ListApiKeys(int id)
    {
        if (!await _sa.TenantExistsAsync(id))
            return NotFound(new { error = "Tenant not found." });

        var keys = await _sa.ListKeysAsync(id);
        return Ok(new { apiKeys = keys });
    }

    [HttpPost("tenants/{id:int}/apikeys")]
    public async Task<IActionResult> CreateApiKey(int id)
    {
        var generated = await _sa.GenerateKeyAsync(id);
        if (generated == null)
            return NotFound(new { error = "Tenant not found." });

        return StatusCode(201, new
        {
            apiKey = generated.Value.apiKey,
            prefix = generated.Value.prefix,
            message = "Store this key now — it is shown only once."
        });
    }

    [HttpPost("tenants/{id:int}/apikeys/{keyId:int}/revoke")]
    public async Task<IActionResult> RevokeApiKey(int id, int keyId)
    {
        var (ok, error) = await _sa.RevokeKeyAsync(id, keyId);
        if (!ok)
            return NotFound(new { error });

        return Ok(new { success = true });
    }
}

public class CreateTeacherRequest
{
    [Required] public string Name { get; set; } = string.Empty;
    public string? OwnerName { get; set; }
    [Required, EmailAddress] public string ContactEmail { get; set; } = string.Empty;
    public string? Phone { get; set; }
    [Required] public string Password { get; set; } = string.Empty;
    public int? MaxShops { get; set; }
    public int? MaxBooks { get; set; }
    public string? Plan { get; set; }
}

public class UpdateTeacherRequest
{
    public string? Name { get; set; }
    public string? OwnerName { get; set; }
    [EmailAddress] public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public int? MaxShops { get; set; }
    public int? MaxBooks { get; set; }
    public string? Plan { get; set; }
}

public class ResetPasswordRequest
{
    [Required] public string NewPassword { get; set; } = string.Empty;
}