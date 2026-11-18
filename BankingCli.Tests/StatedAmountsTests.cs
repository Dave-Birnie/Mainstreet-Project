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
    public void Amounts_the_customer_wrote_are_found(string request, string amount) =>
        Assert.True(StatedAmounts.IsStatedIn(request, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("Move some money from savings to chequing", "10")]
    [InlineData("Deposit 25 into savings", "250")]
    [InlineData("Deposit a couple of dollars", "2")]
    [InlineData("Put fifty into savings", "15")]
    public void Amounts_the_customer_never_wrote_are_not_found(string request, string amount) =>
        Assert.False(StatedAmounts.IsStatedIn(request, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
}
