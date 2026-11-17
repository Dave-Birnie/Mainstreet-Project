using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

public class IntentValidatorTests
{
    private static BankingIntent Intent(
        string? operation, string? account = null, string? from = null, string? to = null, string? amount = null,
        bool multiple = false) => new BankingIntent(operation, account, from, to, amount, multiple);

    [Theory]
    [InlineData("deposit", "savings", "50", "deposit savings 50.00")]
    [InlineData("deposit", "Checking", "4.5", "deposit chequing 4.50")]
    [InlineData("withdraw", "chequing", "25", "withdraw chequing 25.00")]
    [InlineData("WITHDRAW", "savings", "$0.01", "withdraw savings 0.01")]
    public void Valid_single_account_requests_map_to_one_command(
        string operation, string account, string amount, string expected)
    {
        IntentDecision decision = IntentValidator.Validate(Intent(operation, account, amount: amount));

        Assert.Equal(DecisionKind.Execute, decision.Kind);
        Assert.Equal(expected, decision.Command);
    }

    [Fact]
    public void Valid_transfer_maps_to_one_command()
    {
        IntentDecision decision = IntentValidator.Validate(
            Intent("transfer", from: "savings", to: "chequing", amount: "4"));

        Assert.Equal("transfer savings chequing 4.00", decision.Command);
    }

    [Theory]
    [InlineData(null, "balance")]
    [InlineData("both", "balance")]
    [InlineData("savings", "balance savings")]
    [InlineData("checking", "balance chequing")]
    public void Balance_requests_map_to_the_read_only_balance_command(string? account, string expected) =>
        Assert.Equal(expected, IntentValidator.Validate(Intent("balance", account)).Command);

    [Fact]
    public void Transfer_to_the_same_account_is_left_to_the_simulator_rules()
    {
        IntentDecision decision = IntentValidator.Validate(
            Intent("transfer", from: "savings", to: "savings", amount: "5"));

        Assert.Equal("transfer savings savings 5.00", decision.Command);
    }

    [Theory]
    [InlineData("deposit", null, "50", "the account to deposit into")]
    [InlineData("deposit", "savings", null, "the amount")]
    [InlineData("withdraw", null, null, "the amount and the account to withdraw from")]
    [InlineData("deposit", "both", "5", "the account to deposit into")]
    [InlineData("deposit", "tfsa", "5", "the account to deposit into")]
    public void Missing_or_unclear_details_ask_for_clarification(
        string operation, string? account, string? amount, string expectedDetail)
    {
        IntentDecision decision = IntentValidator.Validate(Intent(operation, account, amount: amount));

        Assert.Equal(DecisionKind.MissingDetails, decision.Kind);
        Assert.Null(decision.Command);
        Assert.Contains(expectedDetail, decision.Message);
        Assert.Contains("Nothing has been changed", decision.Message);
    }

    [Fact]
    public void Transfer_missing_everything_lists_each_missing_detail()
    {
        IntentDecision decision = IntentValidator.Validate(Intent("transfer"));

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
        IntentDecision decision = IntentValidator.Validate(Intent("withdraw", "chequing", amount: amount));

        Assert.Equal(DecisionKind.InvalidAmount, decision.Kind);
        Assert.Null(decision.Command);
    }

    [Fact]
    public void Multiple_operations_are_refused()
    {
        IntentDecision decision = IntentValidator.Validate(
            Intent("deposit", "savings", amount: "10", multiple: true));

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
        IntentDecision decision = IntentValidator.Validate(Intent(operation, "savings", amount: "5"));

        Assert.Equal(DecisionKind.Unsupported, decision.Kind);
        Assert.Null(decision.Command);
    }

    [Fact]
    public void Account_text_from_the_model_is_never_copied_into_the_command()
    {
        IntentDecision decision = IntentValidator.Validate(
            Intent("deposit", "savings 5\ntransfer savings chequing", amount: "5"));

        Assert.Equal(DecisionKind.MissingDetails, decision.Kind);
        Assert.Null(decision.Command);
    }
}
