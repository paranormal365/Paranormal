using Ben.Web.Website.Library.Kit;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Prices keep their cents. The pricing page printed the live $19.99 band as "$20" because it
/// formatted with C0, on the assumption that bands were whole dollars (2026-10-01).
/// </summary>
public class BenMoneyTests
{
    [Theory]
    [InlineData(19.99, "$19.99")]
    [InlineData(99.99, "$99.99")]
    [InlineData(20, "$20")]
    [InlineData(29, "$29")]
    [InlineData(1250, "$1,250")]
    [InlineData(0.5, "$0.50")]
    public void A_price_reads_as_it_was_set(decimal amount, string expected)
        => Assert.Equal(expected, BenMoney.Format(amount));
}
