using Ben.Web.Website.Library.Kit;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Covers the one part of the native select that can fail silently: turning an option's value into
/// a string and back. If this is wrong the control shows nothing selected, or writes back a
/// default, with no error anywhere.
/// </summary>
public class SelectValueTests
{
    private sealed record Row(Guid Id, string Name, int Rank);

    private enum Colour { Red, Green }

    [Fact]
    public void Guid_RoundTrips()
    {
        // The case that matters: most of the site's dropdowns are keyed by a Guid.
        var id = Guid.NewGuid();
        var text = SelectValue.ToOptionString(id);

        Assert.True(SelectValue.TryParse<Guid>(text, out var back));
        Assert.Equal(id, back);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(-1)]
    public void Int_RoundTrips(int value)
    {
        Assert.True(SelectValue.TryParse<int>(SelectValue.ToOptionString(value), out var back));
        Assert.Equal(value, back);
    }

    [Fact]
    public void Enum_RoundTrips()
    {
        Assert.True(SelectValue.TryParse<Colour>(SelectValue.ToOptionString(Colour.Green), out var back));
        Assert.Equal(Colour.Green, back);
    }

    [Fact]
    public void String_RoundTrips_IncludingAwkwardCharacters()
    {
        const string value = "a \"quoted\" & <angled> value";
        Assert.True(SelectValue.TryParse<string>(SelectValue.ToOptionString(value), out var back));
        Assert.Equal(value, back);
    }

    [Fact]
    public void NullableGuid_RoundTrips_AndEmptyMeansNothingSelected()
    {
        Guid? id = Guid.NewGuid();
        Assert.True(SelectValue.TryParse<Guid?>(SelectValue.ToOptionString(id), out var back));
        Assert.Equal(id, back);

        // The placeholder option carries an empty value; that has to read back as "no selection"
        // rather than as a parse failure, or picking the placeholder would do nothing.
        Assert.True(SelectValue.TryParse<Guid?>("", out var none));
        Assert.Null(none);
    }

    [Fact]
    public void Decimal_UsesInvariantCulture()
    {
        // A value formatted under one culture and parsed under another is the classic way this
        // breaks; both directions are pinned to invariant.
        Assert.Equal("1.5", SelectValue.ToOptionString(1.5m));
        Assert.True(SelectValue.TryParse<decimal>("1.5", out var back));
        Assert.Equal(1.5m, back);
    }

    [Fact]
    public void UnparseableValue_ReportsFailureRatherThanThrowing()
    {
        Assert.False(SelectValue.TryParse<Guid>("not-a-guid", out var back));
        Assert.Equal(Guid.Empty, back);
    }

    [Fact]
    public void GetMember_ReadsNamedProperties()
    {
        var row = new Row(Guid.NewGuid(), "Nashville", 3);

        Assert.Equal(row.Name, SelectValue.GetMember(row, nameof(Row.Name)));
        Assert.Equal(row.Id,   SelectValue.GetMember(row, nameof(Row.Id)));
        Assert.Equal(row.Rank, SelectValue.GetMember(row, nameof(Row.Rank)));
    }

    [Fact]
    public void GetMember_WithNoName_ReturnsTheItem()
    {
        // How a list of plain strings or enums binds with no TextField/ValueField at all.
        Assert.Equal("plain", SelectValue.GetMember("plain", null));
        Assert.Equal("plain", SelectValue.GetMember("plain", ""));
    }

    [Fact]
    public void GetMember_WithUnknownName_FallsBackToTheItem()
    {
        var row = new Row(Guid.NewGuid(), "Nashville", 3);
        Assert.Equal(row, SelectValue.GetMember(row, "NoSuchProperty"));
    }

    [Fact]
    public void GetMember_IsCaseInsensitive()
    {
        // Call sites pass nameof(...) but a hand-written "name" should not silently miss.
        var row = new Row(Guid.NewGuid(), "Nashville", 3);
        Assert.Equal(row.Name, SelectValue.GetMember(row, "name"));
    }
}
