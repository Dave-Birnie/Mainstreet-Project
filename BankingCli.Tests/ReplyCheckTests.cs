using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

/// <summary>Checks that replies which misstate the simulator's result are caught, and that real replies aren't.</summary>
public class ReplyCheckTests
{
    #region Real replies from gpt-4o-mini are accepted

    // Every reply gpt-4o-mini wrote during manual testing, with the command and result it was explaining.
    // The checks must not reject any of them.
    public static TheoryData<string, string, string> RealReplies => new TheoryData<string, string, string>
    {
        { "balance", "chequing=$4.00 | savings=$46.00",
            "You have $4.00 in your chequing account and $46.00 in your savings account." },
        { "balance savings", "savings=$46.00", "Your savings balance is $46.00." },
        { "deposit savings 1000.00", "DEPOSIT $1000.00 TO savings | savings=$1000.00",
            "Your deposit of $1,000.00 into your savings account was successful, and your savings balance is now $1,000.00." },
        { "deposit savings 1000.00", "DEPOSIT $1000.00 TO savings | savings=$1046.00",
            "Your deposit of $1,000.00 into your savings account was successful, and your new savings balance is $1,046.00." },
        { "deposit savings 50.00", "DEPOSIT $50.00 TO savings | savings=$1096.00",
            "Your deposit of $50.00 to your savings account was successful, and your new balance is $1,096.00." },
        { "deposit savings 50.00", "DEPOSIT $50.00 TO savings | savings=$50.00",
            "You successfully deposited $50.00 into your savings account, and your savings balance is now $50.00." },
        { "deposit savings 50.00", "DEPOSIT $50.00 TO savings | savings=$50.00",
            "You successfully deposited $50.00 into your savings account, which now has a balance of $50.00." },
        { "transfer savings chequing 3.00", "TRANSFER $3.00 FROM savings TO chequing | savings=$7.00 | chequing=$3.00",
            "The transfer of $3.00 from your savings account to your chequing account was successful. Your new savings balance is $7.00, and your chequing balance is $3.00." },
        { "transfer savings chequing 3.00", "TRANSFER $3.00 FROM savings TO chequing | savings=$7.00 | chequing=$3.00",
            "The transfer of $3.00 from your savings account to your chequing account was successful. Your savings balance is now $7.00, and your chequing balance is $3.00." },
        { "transfer savings chequing 4.00", "TRANSFER $4.00 FROM savings TO chequing | savings=$46.00 | chequing=$4.00",
            "The transfer of $4.00 from your savings account to your chequing account was successful. Your new balances are $46.00 in savings and $4.00 in chequing." },
        { "transfer savings chequing 4.00", "TRANSFER $4.00 FROM savings TO chequing | savings=$46.00 | chequing=$4.00",
            "The transfer of $4.00 from your savings account to your chequing account was successful. Your new savings balance is $46.00, and your chequing balance is $4.00." },
        { "transfer savings chequing 4.00", "TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00",
            "The $4.00 has been successfully transferred from your savings account to your chequing account. Your savings balance is now $6.00, and your chequing balance is $4.00." },
        { "transfer savings chequing 4.00", "TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00",
            "The transfer of $4.00 from your savings account to your chequing account was successful. Your savings balance is now $6.00, and your chequing balance is $4.00." },
        { "transfer savings chequing 4.00", "TRANSFER $4.00 FROM savings TO chequing | savings=$996.00 | chequing=$4.00",
            "The transfer of $4.00 from your savings account to your chequing account was successful. Your new savings balance is $996.00, and your chequing balance is $4.00." },
        { "transfer savings savings 5.00", "ERROR SAME_ACCOUNT",
            "The transfer did not happen because you cannot transfer funds between the same account. Your savings balance remains unchanged." },
        { "transfer savings savings 5.00", "ERROR SAME_ACCOUNT",
            "The transfer of $5 from your savings account to your savings account did not happen because a transfer must be between two different accounts. Your balance remains unchanged." },
        { "withdraw chequing 20.00", "ERROR INSUFFICIENT_FUNDS | chequing=$4.00",
            "It looks like the withdrawal did not go through because your chequing account only has a balance of $4.00, which is not enough to take out $20.00." },
        { "withdraw chequing 20.00", "ERROR INSUFFICIENT_FUNDS | chequing=$4.00",
            "It looks like the withdrawal of $20.00 from your chequing account didn't go through because there are insufficient funds. Your current balance in the chequing account is $4.00." },
        { "withdraw chequing 20.00", "ERROR INSUFFICIENT_FUNDS | chequing=$4.00",
            "The withdrawal of $20.00 from your chequing account didn't go through because there are only $4.00 available in that account." },
        { "withdraw chequing 25.00", "ERROR INSUFFICIENT_FUNDS | chequing=$0.00",
            "The withdrawal of $25 from your chequing account did not go through because there are insufficient funds, and your chequing account currently has a balance of $0.00." },
        { "withdraw chequing 25.00", "ERROR INSUFFICIENT_FUNDS | chequing=$0.00",
            "The withdrawal of $25 from your chequing account did not happen because there are insufficient funds; your current balance is $0.00." },
        { "withdraw chequing 25.00", "ERROR INSUFFICIENT_FUNDS | chequing=$10.00",
            "The withdrawal of $25 from your chequing account did not go through because there are insufficient funds; your current chequing balance is $10.00." },
        { "withdraw chequing 25.00", "ERROR INSUFFICIENT_FUNDS | chequing=$10.00",
            "The withdrawal of $25 from your chequing account didn't go through because there are insufficient funds. Your current balance in the chequing account is $10.00." }
    };

    [Theory]
    [MemberData(nameof(RealReplies))]
    public void Real_replies_pass(string command, string simulatorResult, string reply) =>
        Assert.Null(ReplyCheck.FindProblem(reply, command, simulatorResult));

    // Every reply gemini-3.8-flash wrote during manual testing.
    public static TheoryData<string, string, string> RealGeminiReplies => new TheoryData<string, string, string>
    {
        { "balance", "chequing=$4.00 | savings=$46.00",
            "You have $4.00 in your chequing account and $46.00 in your savings account." },
        { "balance savings", "savings=$46.00",
            "Your savings account balance is $46.00." },
        { "deposit savings 1000.00", "DEPOSIT $1000.00 TO savings | savings=$1046.00",
            "I've deposited $1000.00 into your savings account. Your savings balance is now $1046.00." },
        { "deposit savings 50.00", "DEPOSIT $50.00 TO savings | savings=$1096.00",
            "I have deposited $50.00 into your savings account. Your new savings balance is $1096.00." },
        { "deposit savings 50.00", "DEPOSIT $50.00 TO savings | savings=$50.00",
            "I have deposited $50.00 into your savings account. Your new savings balance is $50.00." },
        { "transfer savings chequing 3.00", "TRANSFER $3.00 FROM savings TO chequing | savings=$7.00 | chequing=$3.00",
            "I have transferred $3.00 from your savings to your chequing account. Your new savings balance is $7.00 and your chequing balance is $3.00." },
        { "transfer savings chequing 4.00", "TRANSFER $4.00 FROM savings TO chequing | savings=$46.00 | chequing=$4.00",
            "I have transferred $4.00 from your savings account to your chequing account. Your savings balance is now $46.00, and your chequing balance is $4.00." },
        { "transfer savings chequing 4.00", "TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00",
            "I have transferred $4.00 from your savings to your chequing account. Your savings balance is now $6.00 and your chequing balance is $4.00." },
        { "transfer savings savings 5.00", "ERROR SAME_ACCOUNT",
            "The transfer of $5.00 was declined and no balance changed because a transfer must be between two different accounts." },
        { "withdraw chequing 20.00", "ERROR INSUFFICIENT_FUNDS | chequing=$4.00",
            "Your request to withdraw $20.00 was declined because your chequing account does not have enough funds, and no balance has changed. Your current chequing balance is $4.00." },
        { "withdraw chequing 20.00", "ERROR INSUFFICIENT_FUNDS | chequing=$4.00",
            "Your withdrawal of $20.00 could not be completed because your chequing account does not have enough funds, and no balance has changed. Your current chequing balance is $4.00." },
        { "withdraw chequing 20.00", "ERROR INSUFFICIENT_FUNDS | chequing=$4.00",
            "Your withdrawal of $20.00 could not be completed because your chequing account does not have enough funds, so no balance has changed. Your chequing account balance remains $4.00." },
        { "withdraw chequing 25.00", "ERROR INSUFFICIENT_FUNDS | chequing=$0.00",
            "Your withdrawal of $25.00 was declined because your chequing account does not have enough funds, leaving your balance unchanged at $0.00." },
        { "withdraw chequing 25.00", "ERROR INSUFFICIENT_FUNDS | chequing=$10.00",
            "We could not complete your withdrawal of $25.00 because your chequing account does not have enough funds. No balance has changed, and your chequing balance remains $10.00." },
    };

    [Theory]
    [MemberData(nameof(RealGeminiReplies))]
    public void Real_gemini_replies_pass(string command, string simulatorResult, string reply) =>
        Assert.Null(ReplyCheck.FindProblem(reply, command, simulatorResult));

    // "could not be completed" contains a success word, but it describes a decline.
    [Theory]
    [InlineData("Your withdrawal of $25.00 could not be completed because chequing doesn't have enough money.")]
    [InlineData("Your withdrawal of $25.00 was not successful, because chequing only has $10.00.")]
    [InlineData("The withdrawal of $25.00 wasn't completed, because chequing doesn't have enough. Your chequing balance is still $10.00.")]
    public void Negated_success_words_still_describe_a_decline(string reply) =>
        Assert.Null(ReplyCheck.FindProblem(reply, "withdraw chequing 25.00", "ERROR INSUFFICIENT_FUNDS | chequing=$10.00"));

    #endregion

    #region Replies that misstate the result are caught

    private const string Transfer = "transfer savings chequing 4.00";
    private const string TransferResult = "TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00";

    [Theory]
    [InlineData("I moved $4.00. You now have $60.00 in savings.")]
    [InlineData("You have $10.00 in total.")]
    public void Amounts_the_simulator_never_reported_are_caught(string reply) =>
        Assert.Equal("it mentioned an amount the simulator didn't report",
            ReplyCheck.FindProblem(reply, Transfer, TransferResult));

    // From the ChatGPT review: the right figures attached to the wrong accounts.
    [Theory]
    [InlineData("I moved $4.00. You now have $4.00 in savings and $6.00 in chequing.")]
    [InlineData("Done. Your chequing balance is $6.00 and your savings balance is $4.00.")]
    public void Balances_on_the_wrong_account_are_caught(string reply) =>
        Assert.Equal("it gave a wrong amount or balance", ReplyCheck.FindProblem(reply, Transfer, TransferResult));

    [Fact]
    public void The_requested_amount_only_counts_on_an_account_the_command_used() =>
        Assert.Equal("it gave a wrong amount or balance", ReplyCheck.FindProblem(
            "Done! Your chequing balance is now $10.00.",
            "deposit savings 10.00",
            "DEPOSIT $10.00 TO savings | savings=$10.00"));

    // From the ChatGPT review: a success message after a declined transaction.
    [Theory]
    [InlineData("Done! I withdrew $25.00 from chequing. Your chequing balance is $10.00.")]
    [InlineData("Your withdrawal of $25.00 was successful, although chequing does not show it yet.")]
    public void Success_claimed_for_a_declined_request_is_caught(string reply) =>
        Assert.Equal("it didn't say the request was declined", ReplyCheck.FindProblem(
            reply, "withdraw chequing 25.00", "ERROR INSUFFICIENT_FUNDS | chequing=$10.00"));

    #endregion

    #region Cases from the second review (all were accepted before)

    // After depositing $10 into savings and moving $4 to chequing: savings $6.00, chequing $4.00.
    [Theory]
    [InlineData("Your savings balance is now $4.00.", "it gave a wrong amount or balance")]
    [InlineData("I moved $6.00 from savings to chequing.", "it gave a wrong amount or balance")]
    [InlineData("I moved $4.00 from chequing to savings.", "it named the wrong account")]
    [InlineData("I moved $4.00 to savings.", "it named the wrong account")]
    [InlineData("Your chequing balance is $6.00.", "it gave a wrong amount or balance")]
    public void Wrong_balances_amounts_and_directions_are_caught(string reply, string expected) =>
        Assert.Equal(expected, ReplyCheck.FindProblem(reply, Transfer, TransferResult));

    [Theory]
    [InlineData("I withdrew $25.00 from chequing. You do not need to do anything else.")]
    [InlineData("Done! You do not need to do anything else.")]
    [InlineData("Your $25.00 withdrawal from chequing has been processed.")]
    public void A_declined_request_described_as_done_is_caught(string reply) =>
        Assert.Equal("it didn't say the request was declined", ReplyCheck.FindProblem(
            reply, "withdraw chequing 25.00", "ERROR INSUFFICIENT_FUNDS | chequing=$0.00"));

    [Theory]
    [InlineData("No money was moved, because chequing doesn't have enough to withdraw $25.00.")]
    [InlineData("I couldn't withdraw $25.00 from chequing because there isn't enough money. Your balance there is still $0.00.")]
    public void Plainly_worded_declines_still_pass(string reply) =>
        Assert.Null(ReplyCheck.FindProblem(reply, "withdraw chequing 25.00", "ERROR INSUFFICIENT_FUNDS | chequing=$0.00"));

    #endregion

    #region Cases from the third review

    [Theory]
    [InlineData("I withdrew $4.00.", "it described a different kind of transaction")]
    [InlineData("Your deposit of $4.00 into chequing went through.", "it described a different kind of transaction")]
    [InlineData("I moved six dollars from savings to chequing.", "it wrote an amount in words instead of as a figure")]
    [InlineData("Your savings and chequing balances are $4.00 and $6.00, respectively.", "it gave a wrong amount or balance")]
    public void Different_wording_that_misstates_a_transfer_is_caught(string reply, string expected) =>
        Assert.Equal(expected, ReplyCheck.FindProblem(reply, Transfer, TransferResult));

    [Fact]
    public void A_correct_respectively_reply_passes() =>
        Assert.Null(ReplyCheck.FindProblem(
            "Done! Your savings and chequing balances are now $6.00 and $4.00, respectively.", Transfer, TransferResult));

    [Theory]
    [InlineData("Your withdrawal of $25.00 was declined because your account is frozen.")]
    [InlineData("I couldn't withdraw $25.00 from chequing.")]
    public void A_decline_without_the_real_reason_is_caught(string reply) =>
        Assert.Equal("it gave a different reason for the decline", ReplyCheck.FindProblem(
            reply, "withdraw chequing 25.00", "ERROR INSUFFICIENT_FUNDS | chequing=$0.00"));

    #endregion

    #region Other outcome checks

    [Fact]
    public void A_decline_claimed_for_a_successful_request_is_caught() =>
        Assert.Equal("it said a successful request didn't go through", ReplyCheck.FindProblem(
            "Sorry, I couldn't move $4.00 from savings to chequing.", Transfer, TransferResult));

    [Fact]
    public void A_reply_with_no_figures_is_fine_if_the_outcome_is_right() =>
        Assert.Null(ReplyCheck.FindProblem("Done! Your transfer went through.", Transfer, TransferResult));

    #endregion
}
