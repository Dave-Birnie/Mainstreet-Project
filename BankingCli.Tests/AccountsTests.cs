using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

/// <summary>Checks how account names are recognized.</summary>
public class AccountsTests
{
    [Theory]
    [InlineData("chequing", "Deposit 5 into CHEQUING")]
    [InlineData("chequing", "deposit 5 in my checking account")]
    [InlineData("chequing", "deposit 5 into chq")]
    [InlineData("savings", "deposit 5 into my Saving account")]
    public void Common_ways_of_naming_an_account_are_recognized(string account, string request) =>
        Assert.True(Accounts.IsNamedIn(request, account));

    [Theory]
    [InlineData("chequing", "Deposit 5 into savings")]
    [InlineData("savings", "Deposit 5 into savngs")]
    [InlineData("savings", "Deposit 5 into my rainy-day fund")]
    public void Other_accounts_typos_and_nicknames_do_not_count(string account, string request) =>
        Assert.False(Accounts.IsNamedIn(request, account));

    [Theory]
    [InlineData("Checking", "chequing")]
    [InlineData(" SAVING ", "savings")]
    [InlineData("tfsa", null)]
    [InlineData(null, null)]
    public void Model_values_are_mapped_to_simulator_names(string? value, string? expected) =>
        Assert.Equal(expected, Accounts.Normalize(value));
}
