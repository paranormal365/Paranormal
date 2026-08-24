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

        // Legal name and birth year come from config like everything else about this account.
        // Without them the name backfill would split the display name — and a display name like
        // "AverageBen" yields "AverageBen" with no surname, which is not who anybody is.
        var firstName = config["SeedData:SuperAdmin:FirstName"];
        var lastName  = config["SeedData:SuperAdmin:LastName"];
        var birthYear = config.GetValue<int?>("SeedData:SuperAdmin:BirthYear");

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

        // Ensure the app-wide Admin role exists. Nobody is seeded into it — it is created so a
        // SuperAdmin can assign it, and so `User.IsInRole(RoleNames.Admin)` is answering a real
        // question rather than always being false against a role that does not exist.
        if (!await roleManager.RoleExistsAsync(RoleNames.Admin))
        {
            var adminRoleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(RoleNames.Admin));
            if (!adminRoleResult.Succeeded)
                throw new InvalidOperationException($"Failed to create role '{RoleNames.Admin}': {string.Join(", ", adminRoleResult.Errors.Select(e => e.Description))}");
        }

        // Ensure the Moderator role exists (item 186 F5). Nobody is seeded into it, for the same
        // reason as Admin: it exists so a SuperAdmin can assign it, and so a check against it is
        // answering a real question rather than testing for a role no database row backs.
        if (!await roleManager.RoleExistsAsync(RoleNames.Moderator))
        {
            var moderatorRoleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(RoleNames.Moderator));
            if (!moderatorRoleResult.Succeeded)
                throw new InvalidOperationException($"Failed to create role '{RoleNames.Moderator}': {string.Join(", ", moderatorRoleResult.Errors.Select(e => e.Description))}");
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
                FirstName   = firstName,
                LastName    = lastName,
                BirthYear   = birthYear,
                EmailConfirmed = true,
                DateCreated = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
                throw new InvalidOperationException($"Failed to create SuperAdmin user: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
        }

        // Fill in a legal name the account predates, without ever overwriting one already set.
        //
        // The seeder only supplies these on create, so an account that existed before the columns
        // did would otherwise be left to UserNameBackfillService — which splits the display name,
        // and "AverageBen" splits into a first name of "AverageBen" and no surname. That is not
        // anybody's name. Config knows better, so config wins where the field is still empty.
        //
        // Only where empty: a name the person has since corrected on their profile is theirs, and
        // a seeder that re-imposed config on every restart would undo that silently.
        var needsName = string.IsNullOrWhiteSpace(user.FirstName) && !string.IsNullOrWhiteSpace(firstName);
        var needsBirthYear = user.BirthYear is null && birthYear is not null;

        if (needsName || needsBirthYear)
        {
            if (needsName)
            {
                user.FirstName = firstName;
                user.LastName  = lastName;
            }
            if (needsBirthYear) user.BirthYear = birthYear;

            await userManager.UpdateAsync(user);
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
