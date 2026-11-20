using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

/// <summary>Checks that the model's reading of a request becomes one safe command, or a clear message.</summary>
public class IntentValidatorTests
{
    // By default the request names both accounts and the amount, so tests that aren't about
    // the "the customer must have written it" checks pass them.
    private static IntentDecision Validate(
        string? operation, string? account = null, string? from = null, string? to = null, string? amount = null,
        bool multiple = false, string? request = null) =>
        IntentValidator.Validate(
            new BankingIntent(operation, account, from, to, amount, multiple),
            request ?? $"(a request about chequing and savings for {amount})");

    #region Complete requests become exactly one command

    [Theory]
    [InlineData("deposit", "savings", "50", "deposit savings 50.00")]
    [InlineData("deposit", "Checking", "4.5", "deposit chequing 4.50")]
    [InlineData("withdraw", "chequing", "25", "withdraw chequing 25.00")]
    [InlineData("WITHDRAW", "savings", "$0.01", "withdraw savings 0.01")]
    public void Deposits_and_withdrawals_map_to_one_command(
        string operation, string account, string amount, string expected)
    {
        IntentDecision decision = Validate(operation, account, amount: amount);

        Assert.Equal(DecisionKind.Execute, decision.Kind);
        Assert.Equal(expected, decision.Command);
    }

    [Fact]
    public void Transfer_maps_to_one_command() =>
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

    #endregion

    #region Missing details are asked for

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

    #endregion

    #region Accounts must be named by the customer

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

    // Seen with gpt-4o-mini under an earlier prompt: "savings to savings" was rewritten to savings -> chequing.
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

    [Fact]
    public void Account_text_from_the_model_is_never_copied_into_the_command()
    {
        IntentDecision decision = Validate("deposit", "savings 5\ntransfer savings chequing", amount: "5");

        Assert.Equal(DecisionKind.MissingDetails, decision.Kind);
        Assert.Null(decision.Command);
    }

    #endregion

    #region Amounts must be written by the customer

    // From the ChatGPT review: an amount the model made up must not run.
    [Theory]
    [InlineData("Move some money from savings to chequing", "10")]
    [InlineData("Move 25 dollars from savings to chequing", "250")]
    public void An_amount_the_customer_never_wrote_is_asked_for(string request, string modelAmount)
    {
        IntentDecision decision = Validate(
            "transfer", from: "savings", to: "chequing", amount: modelAmount, request: request);

        Assert.Equal(DecisionKind.MissingDetails, decision.Kind);
        Assert.Contains("I need a bit more information to make that transfer: the amount.", decision.Message);
    }

    [Theory]
    [InlineData("Move four dollars from savings to chequing", "4", "transfer savings chequing 4.00")]
    [InlineData("Move twenty-five bucks from savings to chequing", "25", "transfer savings chequing 25.00")]
    [InlineData("Move $1,000 from savings to chequing", "1000", "transfer savings chequing 1000.00")]
    public void Amounts_in_words_or_digits_are_accepted(string request, string modelAmount, string expected) =>
        Assert.Equal(expected,
            Validate("transfer", from: "savings", to: "chequing", amount: modelAmount, request: request).Command);

    #endregion

    #region Invalid amounts are rejected

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

    private const string HugeAmount = "100000000000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void A_huge_amount_is_explained_as_too_large()
    {
        IntentDecision decision = Validate(
            "deposit", "savings", amount: HugeAmount, request: $"deposit {HugeAmount} to savings");

        Assert.Equal(DecisionKind.InvalidAmount, decision.Kind);
        Assert.Equal("That amount is larger than the simulator can handle. Please try a smaller amount. " +
                     "Nothing has been changed.", decision.Message);
    }

    // Seen with gpt-4o-mini: for a 72-digit amount it returned 75 digits.
    [Fact]
    public void A_huge_amount_is_still_too_large_when_the_model_miscopies_the_digits()
    {
        IntentDecision decision = Validate(
            "deposit", "savings", amount: HugeAmount + "000", request: $"deposit {HugeAmount} to savings");

        Assert.Equal(DecisionKind.InvalidAmount, decision.Kind);
        Assert.StartsWith("That amount is larger than the simulator can handle.", decision.Message);
    }

    [Fact]
    public void A_huge_amount_the_customer_never_wrote_is_asked_for()
    {
        IntentDecision decision = Validate(
            "deposit", "savings", amount: HugeAmount, request: "deposit a lot into savings");

        Assert.Equal(DecisionKind.MissingDetails, decision.Kind);
    }

    // Reading this as a number rounds it to 5.0000..., which used to hide the extra decimal places.
    [Fact]
    public void Decimal_places_are_counted_as_written_not_after_rounding()
    {
        IntentDecision decision = Validate("deposit", "savings", amount: "5.00000000000000000000000000001");

        Assert.Equal(DecisionKind.InvalidAmount, decision.Kind);
        Assert.Contains("no more than two decimal places", decision.Message);
    }

    #endregion

    #region Cases from the second review

    // The model picked $5, but the customer wrote two amounts, so the app asks instead.
    [Fact]
    public void Two_amounts_in_one_request_are_asked_about()
    {
        IntentDecision decision = Validate("deposit", "savings", amount: "5", request: "Deposit $5 or $10 into savings");

        Assert.Equal(DecisionKind.MissingDetails, decision.Kind);
        Assert.Equal("Your request mentions more than one amount ($5.00 and $10.00). Please send it again with just " +
                     "the amount you want to use. Nothing has been changed.", decision.Message);
    }

    [Theory]
    [InlineData("Deposit twelve dollars and fifty cents into savings", "12.50", "deposit savings 12.50")]
    [InlineData("Deposit four point five dollars into savings", "4.5", "deposit savings 4.50")]
    public void Dollars_and_cents_and_decimals_in_words_are_accepted(string request, string modelAmount, string expected) =>
        Assert.Equal(expected, Validate("deposit", "savings", amount: modelAmount, request: request).Command);

    [Fact]
    public void Nonsense_number_words_do_not_crash_the_check()
    {
        string request = "Deposit $1 into savings. one " + string.Join(' ', Enumerable.Repeat("hundred", 15));

        IntentDecision decision = Validate("deposit", "savings", amount: "1", request: request);

        Assert.Equal("deposit savings 1.00", decision.Command);
    }

    [Fact]
    public void Three_decimal_places_are_rejected_like_the_original_command() =>
        Assert.Equal(DecisionKind.InvalidAmount,
            Validate("deposit", "savings", amount: "1.230", request: "Put $1.230 into savings").Kind);

    #endregion

    #region Requests the app can't handle are refused

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

    #endregion
}
