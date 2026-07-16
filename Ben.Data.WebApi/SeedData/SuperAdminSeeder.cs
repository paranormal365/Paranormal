using Ben.Data.Common.Constants;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;

namespace Ben.Data.WebApi.SeedData;

internal static class SuperAdminSeeder
{

    internal static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        var email = config["SeedData:SuperAdmin:Email"];
        var displayName = config["SeedData:SuperAdmin:DisplayName"];
        var password = config["SeedData:SuperAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || password == "REPLACE_ME_WITH_YOUR_PASSWORD")
            return; // Not configured — skip silently

        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        // Ensure SuperAdmin role exists
        if (!await roleManager.RoleExistsAsync(RoleNames.SuperAdmin))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(RoleNames.SuperAdmin));
            if (!roleResult.Succeeded)
                throw new InvalidOperationException($"Failed to create role '{RoleNames.SuperAdmin}': {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
        }

        // Ensure SuperAdmin user exists
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new AppUser
            {
                UserName = email,
                Email = email,
                DisplayName = displayName,
                EmailConfirmed = true,
                DateCreated = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
                throw new InvalidOperationException($"Failed to create SuperAdmin user: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
        }

        // Ensure user is in SuperAdmin role
        if (!await userManager.IsInRoleAsync(user, RoleNames.SuperAdmin))
        {
            var addRoleResult = await userManager.AddToRoleAsync(user, RoleNames.SuperAdmin);
            if (!addRoleResult.Succeeded)
                throw new InvalidOperationException($"Failed to assign role '{RoleNames.SuperAdmin}': {string.Join(", ", addRoleResult.Errors.Select(e => e.Description))}");
        }
    }
}
