using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Which build is actually answering — the one question a deploy could not ask.
/// </summary>
/// <remarks>
/// <para><b>Why this exists (2026-08-26).</b> A deploy ran, reported success, and shipped the
/// previous build. Nothing caught it: <c>deploy-ishaunted.ps1</c>'s smoke checks ask whether the
/// site RESPONDS, never whether it is the build just published, so stale files and a
/// not-recycled app pool both pass cleanly. It was found by hand, comparing an endpoint that
/// should have existed against one that did — which is not a thing anyone should have to do
/// twice.</para>
///
/// <para><b>The running process, not the files on disk.</b> Checking a DLL's timestamp on the
/// server proves the copy happened; it does not prove IIS restarted to serve it. This answers
/// from inside the process that is handling requests, which is the only thing that matters.</para>
///
/// <para><b>Anonymous, deliberately.</b> A deploy verifies before anyone signs in, and the value
/// is a commit hash and a build time — the same information a release tag carries in public. If
/// that is ever judged too much, the alternative is to keep the route and compare server-side
/// against an <c>?expect=</c> parameter, answering only match or mismatch.</para>
/// </remarks>
[ApiController]
[Route("api/public/build")]
[AllowAnonymous]
public sealed class PublicBuildController : ControllerBase
{
    /// <summary>The commit this build came from, and when it was built.</summary>
    [HttpGet]
    public ActionResult<BuildIdentity> Get()
    {
        var assembly = Assembly.GetEntryAssembly();

        // .NET appends "+<SourceRevisionId>" to InformationalVersion when the build supplies one,
        // which is how the commit travels into the binary without a generated source file.
        var informational = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var commit = informational is { } v && v.Contains('+')
            ? v[(v.IndexOf('+') + 1)..]
            : null;

        return Ok(new BuildIdentity(
            Commit:  commit,
            Version: informational,
            // The assembly's own file time: when this binary was produced, which is what a
            // "did my deploy land" question is really asking.
            BuiltUtc: assembly?.Location is { Length: > 0 } path && System.IO.File.Exists(path)
                ? System.IO.File.GetLastWriteTimeUtc(path)
                : null));
    }
}

/// <summary>
/// What is answering. <c>Commit</c> is null on a build that supplied no SourceRevisionId — a
/// developer's F5 — which is itself the honest answer to "is this a deployed build?".
/// </summary>
public sealed record BuildIdentity(string? Commit, string? Version, DateTime? BuiltUtc);
