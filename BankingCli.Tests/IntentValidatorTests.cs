using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

public class IntentValidatorTests
{
    // Mentions both accounts, so tests that are not about account grounding are unaffected by it.
    private const string NamesBothAccounts = "(a request about chequing and savings)";

    private static IntentDecision Validate(
        string? operation, string? account = null, string? from = null, string? to = null, string? amount = null,
        bool multiple = false, string request = NamesBothAccounts) =>
        IntentValidator.Validate(new BankingIntent(operation, account, from, to, amount, multiple), request);

    [Theory]
    [InlineData("deposit", "savings", "50", "deposit savings 50.00")]
    [InlineData("deposit", "Checking", "4.5", "deposit chequing 4.50")]
    [InlineData("withdraw", "chequing", "25", "withdraw chequing 25.00")]
    [InlineData("WITHDRAW", "savings", "$0.01", "withdraw savings 0.01")]
    public void Valid_single_account_requests_map_to_one_command(
        string operation, string account, string amount, string expected)
    {
        IntentDecision decision = Validate(operation, account, amount: amount);

        Assert.Equal(DecisionKind.Execute, decision.Kind);
        Assert.Equal(expected, decision.Command);
    }

    [Fact]
    public void Valid_transfer_maps_to_one_command() =>
        Assert.Equal("transfer savings chequing 4.00",
            Validate("transfer", from: "savings", to: "chequing", amount: "4").Command);

    [Theory]
    [InlineData(null, "balance")]
    [InlineData("both", "balance")]
    [InlineData("savings", "balance savings")]
    [InlineData("checking", "balance chequing")]
    public void Balance_requests_map_to_the_read_only_balance_command(string? account, string expected) =>
        Assert.Equal(expected, Validate("balance", account).Command);

    [Fact]
    public void Transfer_to_the_same_account_is_left_to_the_simulator_rules() =>
        Assert.Equal("transfer savings savings 5.00",
            Validate("transfer", from: "savings", to: "savings", amount: "5").Command);

    [Theory]
    [InlineData("deposit", null, "50", "the account to deposit into")]
    [InlineData("deposit", "savings", null, "the amount")]
    [InlineData("withdraw", null, null, "the amount and the account to withdraw from")]
    [InlineData("deposit", "both", "5", "the account to deposit into")]
    [InlineData("deposit", "tfsa", "5", "the account to deposit into")]
    public void Missing_or_unclear_details_ask_for_clarification(
        string operation, string? account, string? amount, string expectedDetail)
    {
        IntentDecision decision = Validate(operation, account, amount: amount);

        Assert.Equal(DecisionKind.MissingDetails, decision.Kind);
        Assert.Null(decision.Command);
        Assert.Contains(expectedDetail, decision.Message);
        Assert.Contains("Nothing has been changed", decision.Message);
    }

    [Fact]
    public void Transfer_missing_everything_lists_each_missing_detail()
    {
        IntentDecision decision = Validate("transfer");

        Assert.Equal(DecisionKind.MissingDetails, decision.Kind);
        Assert.Contains("the amount, the account to move money from (chequing or savings), and the account to " +
                        "move money to (chequing or savings)", decision.Message);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("10.555")]
    [InlineData("fifty")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    [InlineData("5; withdraw savings 100")]
    public void Invalid_amounts_are_rejected_before_anything_runs(string amount)
    {
        IntentDecision decision = Validate("withdraw", "chequing", amount: amount);

        Assert.Equal(DecisionKind.InvalidAmount, decision.Kind);
        Assert.Null(decision.Command);
    }

    [Fact]
    public void Multiple_operations_are_refused()
    {
        IntentDecision decision = Validate("deposit", "savings", amount: "10", multiple: true);

        Assert.Equal(DecisionKind.MultipleOperations, decision.Kind);
        Assert.Null(decision.Command);
        Assert.Contains("one banking request at a time", decision.Message);
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("open_account")]
    [InlineData("deposit savings 5; transfer")]
    [InlineData(null)]
    public void Unsupported_operations_are_refused(string? operation)
    {
        IntentDecision decision = Validate(operation, "savings", amount: "5");

        Assert.Equal(DecisionKind.Unsupported, decision.Kind);
        Assert.Null(decision.Command);
    }

    [Fact]
    public void Account_text_from_the_model_is_never_copied_into_the_command()
    {
        IntentDecision decision = Validate("deposit", "savings 5\ntransfer savings chequing", amount: "5");

        Assert.Equal(DecisionKind.MissingDetails, decision.Kind);
        Assert.Null(decision.Command);
    }

    // Seen with gpt-4o-mini: for "Transfer ten dollars please" it returned chequing -> savings.
    [Fact]
    public void Transfer_accounts_the_customer_never_named_are_treated_as_missing()
    {
        IntentDecision decision = Validate(
            "transfer", from: "chequing", to: "savings", amount: "10", request: "Transfer ten dollars please");

        Assert.Equal(DecisionKind.MissingDetails, decision.Kind);
        Assert.Contains("the account to move money from (chequing or savings) and the account to move money to",
            decision.Message);
    }

    [Theory]
    [InlineData("Move $4 to chequing", null, "chequing", "the account to move money from")]
    [InlineData("Move $4 to chequing", "savings", "chequing", "the account to move money from")]
    [InlineData("Send 4 bucks to my checking account", "savings", "chequing", "the account to move money from")]
    [InlineData("Move $4 out of my savings", "savings", null, "the account to move money to")]
    [InlineData("Move $4 out of my savings", "savings", "chequing", "the account to move money to")]
    public void A_transfer_that_names_one_account_asks_for_the_other_instead_of_filling_it_in(
        string request, string? from, string? to, string expectedDetail)
    {
        IntentDecision decision = Validate("transfer", from: from, to: to, amount: "4", request: request);

        Assert.Equal(DecisionKind.MissingDetails, decision.Kind);
        Assert.Null(decision.Command);
        Assert.Contains($"I need a bit more information to make that transfer: {expectedDetail}", decision.Message);
    }

    [Fact]
    public void A_stated_same_account_transfer_is_not_rewritten() =>
        Assert.Equal("transfer savings savings 5.00", Validate(
            "transfer", from: "savings", to: "savings", amount: "5",
            request: "Transfer $5 from savings to savings").Command);

    [Theory]
    [InlineData("Deposit $20")]
    [InlineData("Deposit $20 into my TFSA")]
    [InlineData("Deposit $20 into savngs")]
    public void Single_account_the_customer_never_named_is_treated_as_missing(string request)
    {
        IntentDecision decision = Validate("deposit", "savings", amount: "20", request: request);

        Assert.Equal(DecisionKind.MissingDetails, decision.Kind);
        Assert.Contains("the account to deposit into", decision.Message);
    }

    [Fact]
    public void Balance_for_an_account_the_customer_never_named_shows_both_balances() =>
        Assert.Equal("balance", Validate("balance", "savings", request: "What's my balance?").Command);

    [Theory]
    [InlineData("chequing", "Deposit 5 into CHEQUING")]
    [InlineData("chequing", "deposit 5 in my checking account")]
    [InlineData("chequing", "deposit 5 into chq")]
    [InlineData("savings", "deposit 5 into my Saving account")]
    public void Common_ways_of_naming_an_account_are_recognized(string account, string request) =>
        Assert.True(IntentValidator.IsNamedIn(request, account));
}
