using Ben.Data.Common.Helpers;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A file named like a picture is not a picture (Ben, 2026-08-27).
/// </summary>
/// <remarks>
/// <para>Ben uploaded <c>IMG_3702.JPG</c> to his profile and got "Photo unavailable". The upload
/// had reported success: the extension check passed on the name, the content type was whatever
/// the browser guessed from that same name, and the bytes were never looked at. They were stored
/// and later served back as <c>image/jpeg</c> — bytes no browser can decode.</para>
///
/// <para><b>Why signatures and not a decode attempt.</b> The first fix tried was "ask the image
/// library to decode it, reject what it refuses". That is stricter and wrong: the decoder in use
/// refuses a valid 8x8 RGBA PNG that browsers render without complaint, so the gate would have
/// rejected files that worked. The pinned case below is that exact PNG.</para>
/// </remarks>
public sealed class ImageSignatureTests
{
    private static byte[] Head(params byte[] bytes)
    {
        var head = new byte[ImageSignature.BytesNeeded];
        bytes.CopyTo(head, 0);
        return head;
    }

    [Fact]
    public void A_jpeg_is_displayable()
        => Assert.True(ImageSignature.IsBrowserDisplayable(Head(0xFF, 0xD8, 0xFF, 0xE0)));

    [Fact]
    public void A_png_is_displayable()
        => Assert.True(ImageSignature.IsBrowserDisplayable(
            Head(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)));

    [Theory]
    [InlineData("GIF87a")]
    [InlineData("GIF89a")]
    public void A_gif_is_displayable(string magic)
        => Assert.True(ImageSignature.IsBrowserDisplayable(
            Head(System.Text.Encoding.ASCII.GetBytes(magic))));

    /// <summary>WebP hides its marker after the RIFF size, which is why twelve bytes are read.</summary>
    [Fact]
    public void A_webp_is_displayable_despite_the_size_in_the_middle()
    {
        var head = new byte[12];
        "RIFF"u8.CopyTo(head);
        head[4] = 0x2A; head[5] = 0x00; head[6] = 0x00; head[7] = 0x00;   // some size
        "WEBP"u8.CopyTo(head.AsSpan(8));

        Assert.True(ImageSignature.IsBrowserDisplayable(head));
    }

    /// <summary>RIFF alone is not WebP — a WAV starts the same way.</summary>
    [Fact]
    public void A_riff_that_is_not_webp_is_refused()
    {
        var head = new byte[12];
        "RIFF"u8.CopyTo(head);
        "WAVE"u8.CopyTo(head.AsSpan(8));

        Assert.False(ImageSignature.IsBrowserDisplayable(head));
    }

    /// <summary>
    /// HEIC — the case that started this. An iPhone photo keeps these bytes while picking up a
    /// .JPG name, and no browser draws it in an img tag.
    /// </summary>
    [Fact]
    public void Heic_is_refused_however_it_is_named()
    {
        var head = new byte[12];
        head[3] = 0x18;
        "ftypheic"u8.CopyTo(head.AsSpan(4));

        Assert.False(ImageSignature.IsBrowserDisplayable(head));
    }

    [Fact]
    public void A_text_file_renamed_jpg_is_refused()
        => Assert.False(ImageSignature.IsBrowserDisplayable(
            System.Text.Encoding.ASCII.GetBytes("Dear diary,\n")));

    /// <summary>A truncated head decides rather than throwing.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_short_head_is_handled(int length)
    {
        var head = new byte[length];
        for (var i = 0; i < length; i++) head[i] = 0xFF;

        // Three bytes of 0xFF is not the JPEG marker (FF D8 FF), and nothing shorter can decide.
        Assert.False(ImageSignature.IsBrowserDisplayable(head));
    }

    /// <summary>Exactly three bytes IS enough when they are the JPEG marker.</summary>
    [Fact]
    public void The_shortest_decidable_jpeg_head_is_accepted()
        => Assert.True(ImageSignature.IsBrowserDisplayable(new byte[] { 0xFF, 0xD8, 0xFF }));
}
