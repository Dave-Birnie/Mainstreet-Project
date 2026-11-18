using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

/// <summary>Checks what the reply writer sends to the model, and that a reply that disagrees with the simulator is rejected.</summary>
public class ReplyWriterTests
{
    private const string TransferResult = "TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00";

    [Fact]
    public async Task Sends_the_real_result_and_returns_the_reply()
    {
        FakeChatClient chat = new FakeChatClient()
            .Returns("All set! I moved $4.00 from savings to chequing. You now have $6.00 in savings and $4.00 in chequing.");

        string reply = await new ReplyWriter(chat).WriteAsync(
            "Move four dollars from savings to chequing", "transfer savings chequing 4.00", TransferResult, default);

        Assert.StartsWith("All set!", reply);
        (IReadOnlyList<ChatMessage> messages, bool requireJson) = Assert.Single(chat.Calls);
        Assert.False(requireJson);
        Assert.Contains($"Simulator result: {TransferResult}", messages[1].Content);
    }

    [Fact]
    public async Task Declines_include_the_meaning_of_the_error_code()
    {
        FakeChatClient chat = new FakeChatClient().Returns("Sorry, chequing only has $0.00, so I couldn't withdraw $25.00.");

        await new ReplyWriter(chat).WriteAsync(
            "Withdraw $25 from chequing", "withdraw chequing 25.00", "ERROR INSUFFICIENT_FUNDS | chequing=$0.00", default);

        Assert.Contains("Meaning of INSUFFICIENT_FUNDS: The account the money would come from does not have enough funds",
            chat.Calls[0].Messages[1].Content);
    }

    [Fact]
    public async Task A_reply_that_disagrees_with_the_simulator_is_rejected_and_kept_for_the_status_command()
    {
        FakeChatClient chat = new FakeChatClient().Returns("Done! You now have $1,000,000.00 in savings.");

        AiServiceException error = await Assert.ThrowsAsync<AiServiceException>(() => new ReplyWriter(chat).WriteAsync(
            "Move four dollars from savings to chequing", "transfer savings chequing 4.00", TransferResult, default));

        Assert.Equal("the AI's reply didn't match the simulator's result " +
                     "(it mentioned an amount the simulator didn't report), so I didn't show it", error.Problem);
        Assert.Equal("Discarded reply: Done! You now have $1,000,000.00 in savings.", error.Details);
    }
}
