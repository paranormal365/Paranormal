using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// How an uploaded file is handed back: a recording plays where it sits, a document is saved.
/// </summary>
/// <remarks>
/// Ben put an 8.4 MB mp4 on a case on 2026-09-17 and got a black rectangle with dead controls,
/// which reads exactly like an upload that silently failed — the file was there the whole time.
/// Safari will not start a &lt;video&gt; whose source cannot answer a byte range: it asks for the
/// first bytes, is handed the whole file with a 200, and gives up. The field session's own file
/// route had always answered ranges; this one, which serves every other upload on the site, had
/// not, and nothing tested it.
/// </remarks>
[TestFixture]
[Category("MediaServing")]
public class MediaServingTests : BenTestBase
{
    private static byte[] TinyMp4()
    {
        // A file the server will store and hand back; the bytes need only be stable, because what
        // is under test is the response, not the picture.
        var bytes = new byte[64 * 1024];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i % 251);
        return bytes;
    }

    private async Task<(IAPIRequestContext Api, string Token, string Org, string Case)> SignedInAsync()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new()
        {
            DataObject = new { email = UserEmail, password = UserPassword },
        });
        Assert.That(login.Ok, Is.True, "the seeded account should sign in");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()!;

        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
        var orgs = await api.GetAsync("/api/organizations", new() { Headers = headers });
        var org = (await orgs.JsonAsync())!.Value[0].GetProperty("id").GetString()!;
        var cases = await api.GetAsync($"/api/organizations/{org}/cases", new() { Headers = headers });
        var which = (await cases.JsonAsync())!.Value[0].GetProperty("id").GetString()!;
        return (api, token, org, which);
    }

    private static async Task<string> UploadAsync(IAPIRequestContext api, string token, string org, string which,
                                                  string name, string contentType)
    {
        var form = api.CreateFormData();
        form.Append("file", new FilePayload { Name = name, MimeType = contentType, Buffer = TinyMp4() });
        var upload = await api.PostAsync($"/api/orgs/{org}/cases/{which}/files", new()
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            Multipart = form,
        });
        Assert.That(upload.Ok, Is.True, $"upload failed: {await upload.TextAsync()}");
        return (await upload.JsonAsync())!.Value.GetProperty("uploadFileId").GetString()!;
    }

    [Test]
    public async Task A_video_answers_a_byte_range_so_it_can_play()
    {
        var (api, token, org, which) = await SignedInAsync();
        var fileId = await UploadAsync(api, token, org, which, "range-check.mp4", "video/mp4");

        var partial = await api.GetAsync($"/api/upload-files/{fileId}/download", new()
        {
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}",
                ["Range"] = "bytes=0-1023",
            },
        });

        Assert.That(partial.Status, Is.EqualTo(206), "a video that cannot serve a range will not play in Safari");
        Assert.That(partial.Headers["content-range"], Does.StartWith("bytes 0-1023/"));
        Assert.That((await partial.BodyAsync()).Length, Is.EqualTo(1024));
    }

    [Test]
    public async Task A_video_is_played_in_the_page_rather_than_saved()
    {
        var (api, token, org, which) = await SignedInAsync();
        var fileId = await UploadAsync(api, token, org, which, "inline-check.mp4", "video/mp4");

        var whole = await api.GetAsync($"/api/upload-files/{fileId}/download", new()
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });

        Assert.That(whole.Status, Is.EqualTo(200));
        Assert.That(whole.Headers["accept-ranges"], Is.EqualTo("bytes"));
        Assert.That(whole.Headers.ContainsKey("content-disposition"), Is.False,
                    "naming the file makes it an attachment, which is not how a recording beside its case should arrive");
    }

    /// <summary>The other half: a document still arrives as a download, under its own name.</summary>
    [Test]
    public async Task A_document_is_still_saved_under_its_name()
    {
        var (api, token, org, which) = await SignedInAsync();
        var fileId = await UploadAsync(api, token, org, which, "statement.pdf", "application/pdf");

        var whole = await api.GetAsync($"/api/upload-files/{fileId}/download", new()
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });

        Assert.That(whole.Status, Is.EqualTo(200));
        Assert.That(whole.Headers["content-disposition"], Does.Contain("statement.pdf"));
        Assert.That(whole.Headers["accept-ranges"], Is.EqualTo("bytes"), "a big download should still be resumable");
    }
}
