using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Cms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Contact details, a file gallery and the member roster, built from a group's own records (backlog 256).
/// </summary>
/// <remarks>
/// What matters is what never comes through: a private phone, a hidden email, a street the group chose not to
/// show, a members-only file, and a member who didn't agree to be named.
/// </remarks>
public sealed class CmsGroupSectionsTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Agreed = Guid.NewGuid();
    private static readonly Guid Declined = Guid.NewGuid();
    private static readonly Guid PublicFile = Guid.NewGuid();
    private static readonly Guid PrivateFile = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> Factory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync()
    {
        var f = Factory();
        await using var db = await f.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        db.Organizations.Add(new Organization { Id = Org, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = now, PublicPhone = "615-555-0100" });

        var phoneType = Guid.NewGuid();
        db.OrganizationPhoneTypes.Add(new OrganizationPhoneType { Id = phoneType, Name = "Office", DateCreated = now });
        db.OrganizationPhones.Add(new OrganizationPhone { Id = Guid.NewGuid(), OrganizationId = Org, OrganizationPhoneTypeId = phoneType, PhoneNumber = "615-555-0199", IsPublic = true, ValidationToken = "", DateCreated = now });
        db.OrganizationPhones.Add(new OrganizationPhone { Id = Guid.NewGuid(), OrganizationId = Org, OrganizationPhoneTypeId = phoneType, PhoneNumber = "615-555-0666", IsPublic = false, ValidationToken = "", DateCreated = now });
        db.OrganizationEmails.Add(new OrganizationEmail { Id = Guid.NewGuid(), OrganizationId = Org, EmailAddress = "hello@ghostsquad.test", IsPublic = true, DateCreated = now });
        db.OrganizationEmails.Add(new OrganizationEmail { Id = Guid.NewGuid(), OrganizationId = Org, EmailAddress = "hidden@ghostsquad.test", IsPublic = true, IsHidden = true, DateCreated = now });
        db.OrganizationLinks.Add(new OrganizationLink { Id = Guid.NewGuid(), OrganizationId = Org, LinkUrl = "javascript:alert(1)", IsPublic = true, IsActive = true, DateCreated = now });
        db.OrganizationLinks.Add(new OrganizationLink { Id = Guid.NewGuid(), OrganizationId = Org, LinkUrl = "ghostsquad.test", DisplayText = "Our site", IsPublic = true, IsActive = true, DateCreated = now });

        OrganizationAddress Address(string street, OrganizationAddressVisibility v, OrganizationAddressDisplayMode mode) => new()
        {
            Id = Guid.NewGuid(), OrganizationId = Org, StreetAddress1 = street, City = "Nashville", State = "TN",
            ZipCode = "37201", Country = "US", Visibility = v, PublicDisplayMode = mode, DateCreated = now,
        };
        db.OrganizationAddresses.Add(Address("1 Full Street", OrganizationAddressVisibility.Public, OrganizationAddressDisplayMode.FullAddressOnly));
        db.OrganizationAddresses.Add(Address("2 Region Road", OrganizationAddressVisibility.Public, OrganizationAddressDisplayMode.RegionOnly));
        db.OrganizationAddresses.Add(Address("3 Private Lane", OrganizationAddressVisibility.Private, OrganizationAddressDisplayMode.FullAddressOnly));
        db.OrganizationAddresses.Add(Address("4 Hidden Way", OrganizationAddressVisibility.Public, OrganizationAddressDisplayMode.Hidden));

        foreach (var (id, isPublic) in new[] { (PublicFile, true), (PrivateFile, false) })
            db.UploadFiles.Add(new UploadFile { Id = id, FileName = $"{id}.jpg", StoredFileName = "x", ContentType = "image/jpeg", IsPublic = isPublic, DateCreated = now });

        foreach (var (id, name, agreed) in new[] { (Agreed, "Ada Agreed", true), (Declined, "Dee Declined", false) })
        {
            db.AppUsers.Add(new AppUser { Id = id, DisplayName = name, DateCreated = now });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            { Id = Guid.NewGuid(), OrganizationId = Org, AppUserId = id, Role = OrganizationMemberRole.Member, IsActive = true, ShowOnPublicPages = agreed, DateCreated = now });
        }
        db.AppUserPhotos.Add(new AppUserPhoto { Id = Guid.NewGuid(), AppUserId = Agreed, UploadFileId = PublicFile, IsPublic = true, IsActive = true, DateCreated = now });

        await db.SaveChangesAsync();
        return f;
    }

    private static async Task<JsonElement> ResolveAsync(IDbContextFactory<BenDataContext> f, CmsSectionType type, string json)
    {
        await using var db = await f.CreateDbContextAsync();
        return JsonDocument.Parse(await CmsEmbed.ResolveAsync(db, Org, type, json, default)).RootElement.Clone();
    }

    [Fact]
    public async Task Contact_details_show_only_what_the_group_marked_public()
    {
        var text = (await ResolveAsync(await SeedAsync(), CmsSectionType.ContactInfo, "{}")).GetRawText();

        Assert.Contains("615-555-0100", text);      // the group's own public phone
        Assert.Contains("615-555-0199", text);
        Assert.Contains("hello@ghostsquad.test", text);
        Assert.Contains("https://ghostsquad.test/", text);
        Assert.Contains("1 Full Street", text);
        Assert.Contains("Nashville, TN", text);       // the region-only address, as its town

        Assert.DoesNotContain("0666", text);           // private phone
        Assert.DoesNotContain("hidden@", text);        // hidden email
        Assert.DoesNotContain("javascript", text);     // not a web address
        Assert.DoesNotContain("Region Road", text);    // street of a region-only address
        Assert.DoesNotContain("Private Lane", text);
        Assert.DoesNotContain("Hidden Way", text);
    }

    [Fact]
    public async Task Contact_details_leave_out_the_kinds_switched_off()
    {
        var text = (await ResolveAsync(await SeedAsync(), CmsSectionType.ContactInfo,
            """{"showAddresses":false,"showEmails":true,"showPhones":false,"showLinks":false}""")).GetRawText();
        Assert.Contains("hello@ghostsquad.test", text);
        Assert.DoesNotContain("615-555", text);
        Assert.DoesNotContain("Full Street", text);
    }

    [Fact]
    public async Task A_gallery_shows_public_files_only_in_the_groups_order()
    {
        var rows = await ResolveAsync(await SeedAsync(), CmsSectionType.FileGallery,
            JsonSerializer.Serialize(new { uploadFileIds = new[] { PrivateFile, PublicFile } }));
        var id = Assert.Single(rows.EnumerateArray()).GetProperty("id").GetGuid();
        Assert.Equal(PublicFile, id);
    }

    [Fact]
    public async Task The_roster_names_only_members_who_agreed()
    {
        var f = await SeedAsync();
        var everyone = await ResolveAsync(f, CmsSectionType.MemberRoster, "{}");
        var person = Assert.Single(everyone.EnumerateArray());
        Assert.Equal("Ada Agreed", person.GetProperty("name").GetString());
        Assert.Equal(PublicFile, person.GetProperty("photoFileId").GetGuid());

        // Choosing somebody who didn't agree still doesn't name them.
        var chosen = await ResolveAsync(f, CmsSectionType.MemberRoster,
            JsonSerializer.Serialize(new { everyone = false, memberIds = new[] { Declined, Agreed } }));
        Assert.Equal(["Ada Agreed"], chosen.EnumerateArray().Select(p => p.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_member_turns_their_own_listing_on_and_nobody_elses()
    {
        var f = await SeedAsync();
        MyPublicListingController As(Guid id) => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Bearer")) },
            },
        };

        Assert.IsType<NoContentResult>(await As(Declined).Set(Org, new SetMyPublicListing(true), default));
        Assert.IsType<NotFoundResult>(await As(Guid.NewGuid()).Set(Org, new SetMyPublicListing(true), default));

        var mine = Assert.IsAssignableFrom<IEnumerable<MyPublicListing>>(
            Assert.IsType<OkObjectResult>((await As(Declined).Get(default)).Result).Value);
        Assert.True(Assert.Single(mine).ShowOnPublicPages);

        var roster = await ResolveAsync(f, CmsSectionType.MemberRoster, "{}");
        Assert.Equal(2, roster.GetArrayLength());
    }
}
