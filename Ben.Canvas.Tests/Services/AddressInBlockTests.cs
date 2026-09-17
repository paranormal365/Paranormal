using Ben.Canvas.Core.Model;
using Ben.Canvas.Editor.Services;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// Which blocks have an address in them, and so are offered a map of it.
/// </summary>
/// <remarks>
/// Ben, 2026-09-17: an address pasted into a card should be able to become a map. The offer lives on
/// the block's menu and appears only when this finds something, so what this returns is exactly what
/// decides whether the item is there.
/// </remarks>
public sealed class AddressInBlockTests
{
    [Fact]
    public void A_note_that_is_an_address_offers_it() =>
        Assert.Equal("1425 Old Highway 31W, Red Boiling Springs, TN 37150",
                     AddressInBlock.Of(new TextData { Text = "1425 Old Highway 31W, Red Boiling Springs, TN 37150" }));

    [Fact]
    public void An_address_on_its_own_line_inside_a_note_is_found()
    {
        var note = new TextData
        {
            Text = "Owner rang about the cellar door.\n520 Lake Cook Road\nShe is there on Tuesdays.",
        };

        Assert.Equal("520 Lake Cook Road", AddressInBlock.Of(note));
    }

    [Fact]
    public void A_card_offers_the_address_in_whichever_field_holds_it()
    {
        var card = new CardData
        {
            TemplateId = "person",
            Title = "Susan Holt",
            Fields = { ["role"] = "Witness", ["connection"] = "12A Church Street, Franklin" },
        };

        Assert.Equal("12A Church Street, Franklin", AddressInBlock.Of(card));
    }

    /// <summary>
    /// A sentence with a town and a ZIP in it is recognised whole, and handed over whole.
    /// </summary>
    /// <remarks>
    /// Trimming it down to what looks like the address means guessing where the town starts, and
    /// guessing wrong turns "Red Boiling Springs" into "Springs". The geocoder reads a few extra
    /// words far better than it reads half a town's name, and the map is labelled with what the
    /// person actually wrote.
    /// </remarks>
    [Fact]
    public void A_sentence_with_a_town_and_a_zip_in_it_is_offered_as_written() =>
        Assert.Equal("We are at Red Boiling Springs, TN 37150",
                     AddressInBlock.Of(new MessageData { Html = "<p>We are at Red Boiling Springs, TN 37150</p>" }));

    /// <summary>
    /// Where the whole line is not an address, leading words are dropped until one begins: a house
    /// number has to start what it is part of, so this is the case trimming exists for.
    /// </summary>
    [Fact]
    public void An_address_a_few_words_in_is_found_from_its_house_number() =>
        Assert.Equal("12A Church Street, Franklin until 1994",
                     AddressInBlock.Of(new TextData { Text = "Lived at 12A Church Street, Franklin until 1994" }));

    /// <summary>The cap stops a paragraph being tried every possible way for nothing.</summary>
    [Fact]
    public void An_address_buried_deep_in_a_paragraph_is_left_alone() =>
        Assert.Null(AddressInBlock.Of(new TextData
        {
            Text = "She said that the whole thing started the week they moved into 12A Church Street, Franklin.",
        }));

    [Fact]
    public void An_images_caption_counts_because_it_is_written_by_hand() =>
        Assert.Equal("1600 Pennsylvania Avenue NW, Washington, DC 20500",
                     AddressInBlock.Of(new ImageData { Caption = "1600 Pennsylvania Avenue NW, Washington, DC 20500" }));

    [Theory]
    [InlineData("Chapter 3, page 41")]
    [InlineData("Three knocks on the cellar door at 2am, twice that week.")]
    [InlineData("")]
    public void A_block_with_no_address_is_offered_nothing(string text) =>
        Assert.Null(AddressInBlock.Of(new TextData { Text = text }));

    /// <summary>A map already knows where it is; a file is named rather than written.</summary>
    [Fact]
    public void A_map_and_a_file_are_never_offered_a_map()
    {
        Assert.Null(AddressInBlock.Of(new MapData { Address = "1425 Old Highway 31W, Red Boiling Springs, TN 37150" }));
        Assert.Null(AddressInBlock.Of(new FileData { FileName = "520 Lake Cook Road survey.pdf" }));
    }

    [Fact]
    public void Nothing_is_offered_for_nothing() => Assert.Null(AddressInBlock.Of(null));
}
