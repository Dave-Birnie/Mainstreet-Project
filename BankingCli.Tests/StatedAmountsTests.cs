using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

/// <summary>Checks that amounts written in digits or words are found, so a made-up amount can be spotted.</summary>
public class StatedAmountsTests
{
    [Theory]
    [InlineData("Withdraw $25 from chequing", "25")]
    [InlineData("Deposit 4.5 into savings", "4.50")]
    [InlineData("Deposit $1,000 into savings", "1000")]
    [InlineData("Deposit -5 dollars", "-5")]
    [InlineData("Put fifty dollars into savings", "50")]
    [InlineData("Move four dollars from savings to chequing", "4")]
    [InlineData("Move twenty-five bucks to chequing", "25")]
    [InlineData("Deposit one hundred and five dollars", "105")]
    [InlineData("Deposit a hundred bucks", "100")]
    [InlineData("Deposit two thousand five hundred dollars", "2500")]
    [InlineData("Deposit one million dollars", "1000000")]
    [InlineData("Deposit a dollar into savings", "1")]
    [InlineData("Deposit 50bucks into savings", "50")]
    [InlineData("Deposit twelve dollars and fifty cents into savings", "12.50")]
    [InlineData("Deposit 12 dollars and 50 cents into savings", "12.50")]
    [InlineData("Deposit four point five dollars into savings", "4.5")]
    [InlineData("Deposit four point seven five into savings", "4.75")]
    [InlineData("transfer 1k from checking to savings", "1000")]
    [InlineData("Move 2.5K from savings to chequing", "2500")]
    [InlineData("Deposit $3 thousand into savings", "3000")]
    [InlineData("Withdraw 5 grand from savings", "5000")]
    [InlineData("Deposit 2 million into savings", "2000000")]
    public void Amounts_the_customer_wrote_are_found(string request, string amount) =>
        Assert.True(StatedAmounts.IsStatedIn(request, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("Move some money from savings to chequing", "10")]
    [InlineData("Deposit 25 into savings", "250")]
    [InlineData("Deposit a couple of dollars", "2")]
    [InlineData("Put fifty into savings", "15")]
    [InlineData("Deposit 1kg of gold", "1000")]
    public void Amounts_the_customer_never_wrote_are_not_found(string request, string amount) =>
        Assert.False(StatedAmounts.IsStatedIn(request, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("Deposit $5 or $10 into savings", new[] { 5.0, 10.0 })]
    [InlineData("transfer 1k from checking to savings", new[] { 1000.0 })]
    [InlineData("Deposit twelve dollars and fifty cents into savings", new[] { 12.5 })]
    [InlineData("Move $5 (five dollars) to chequing", new[] { 5.0 })]
    [InlineData("How much do I have?", new double[0])]
    public void Each_written_amount_is_counted_once(string request, double[] expected) =>
        Assert.Equal(expected.Select(value => (decimal)value), StatedAmounts.AmountsIn(request));

    // From the second review: this used to crash the app with an OverflowException.
    [Fact]
    public void Repeated_multipliers_do_not_overflow()
    {
        string request = "Deposit $1 into savings. one " + string.Join(' ', Enumerable.Repeat("hundred", 15));

        Assert.Equal([1m], StatedAmounts.AmountsIn(request));
        Assert.True(StatedAmounts.HasNumberTooLargeToRead(request));
    }
}
