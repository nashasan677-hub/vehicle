using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleFleetMS.Data;
using VehicleFleetMS.Helpers;
using VehicleFleetMS.Models;

namespace VehicleFleetMS.Pages;

public class UsersModel(FleetDbContext db) : PageModel
{
    public List<AppUser> Users { get; set; } = [];
    public List<AuditLogItem> AuditLog { get; set; } = [];
    public string? RemoveError { get; set; }
    public string? InviteError { get; set; }
    public string? InviteLink { get; set; }
    public int ActiveCount { get; set; }
    public int InvitedCount { get; set; }
    public bool CanApprove => User.FindFirst(ClaimTypes.Role)?.Value == "System Administrator";

    [TempData]
    public string? InviteLinkFlash { get; set; }

    public async Task OnGetAsync(string? removeError, string? inviteError)
    {
        Users = await db.Users.OrderBy(u => u.Name).ToListAsync();
        AuditLog = await db.AuditLog.OrderByDescending(a => a.OccurredAt).Take(20).ToListAsync();
        ActiveCount = Users.Count(u => u.Status == "Active");
        InvitedCount = Users.Count(u => u.Status == "Invited");
        RemoveError = removeError switch
        {
            "self" => "You can't remove your own account while signed in to it.",
            "self-role" => "You can't change your own role away from System Administrator while signed in to it.",
            _ => null,
        };
        InviteError = inviteError switch
        {
            "email" => "That email is already in use by another account.",
            "username" => "That username is already taken.",
            "password" => "Password must be at least 8 characters.",
            _ => null,
        };
        InviteLink = InviteLinkFlash;
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var users = await db.Users.OrderBy(u => u.Name).ToListAsync();
        var excel = ExcelExport.Build(
            ["Name", "Email", "Role", "Department", "Status", "Last Active"],
            users.Select(u => new object?[] { u.Name, u.Email, u.Role, u.Department, u.Status, u.LastActiveAt }));
        return File(excel, ExcelExport.ContentType, $"users-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    public async Task<IActionResult> OnPostInviteAsync(string name, string email, string username, string password, string role, string department)
    {
        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return RedirectToPage(new { inviteError = "email" });
        }
        if (await db.Users.AnyAsync(u => u.Username == username))
        {
            return RedirectToPage(new { inviteError = "username" });
        }
        if (password.Length < 8)
        {
            return RedirectToPage(new { inviteError = "password" });
        }

        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p[0])).ToUpperInvariant();
        var user = new AppUser { Name = name, Initials = initials.Length > 2 ? initials[..2] : initials, Email = email, Username = username, Role = role, Department = department, Status = "Invited" };
        var hasher = new PasswordHasher<AppUser>();
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        db.AuditLog.Add(new AuditLogItem { Actor = User.Identity?.Name ?? "System", Action = "Invited new user", Target = email, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "—" });
        await db.SaveChangesAsync();

        InviteLinkFlash = Url.Page("/Login", null, new { username, password }, Request.Scheme);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        if (!CanApprove)
        {
            return Forbid();
        }

        var user = await db.Users.FindAsync(id);
        if (user is not null && user.Status == "Invited")
        {
            user.Status = "Active";
            db.AuditLog.Add(new AuditLogItem { Actor = User.Identity?.Name ?? "System", Action = "Approved user account", Target = user.Email, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "—" });
            await db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetRoleAsync(int id, string role)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (currentUserId == id.ToString() && role != "System Administrator")
        {
            return RedirectToPage(new { removeError = "self-role" });
        }

        var user = await db.Users.FindAsync(id);
        if (user is not null)
        {
            user.Role = role;
            db.AuditLog.Add(new AuditLogItem { Actor = User.Identity?.Name ?? "System", Action = $"Changed role to {role}", Target = user.Email, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "—" });
            await db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (currentUserId == id.ToString())
        {
            return RedirectToPage(new { removeError = "self" });
        }

        var user = await db.Users.FindAsync(id);
        if (user is not null)
        {
            db.AuditLog.Add(new AuditLogItem { Actor = User.Identity?.Name ?? "System", Action = "Removed user account", Target = user.Email, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "—" });
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
        return RedirectToPage();
    }
}
