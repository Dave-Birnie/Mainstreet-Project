using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

public class IntentInterpreterTests
{
    [Fact]
    public async Task Sends_the_request_as_user_data_and_asks_for_json()
    {
        FakeChatClient chat = new FakeChatClient().ReturnsIntent("deposit", "savings", amount: "50");

        BankingIntent intent = await new IntentInterpreter(chat).InterpretAsync("Put fifty dollars into savings", default);

        Assert.Equal(new BankingIntent("deposit", "savings", null, null, "50", false), intent);
        (IReadOnlyList<ChatMessage> messages, bool requireJson) = Assert.Single(chat.Calls);
        Assert.True(requireJson);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(ChatMessage.User("Put fifty dollars into savings"), messages[1]);
    }

    [Fact]
    public void Reads_json_wrapped_in_a_code_fence_and_numeric_amounts_without_losing_precision()
    {
        BankingIntent intent = IntentInterpreter.Parse("""
            ```json
            {"operation":"transfer","from_account":"savings","to_account":"chequing","amount":0.10,"multiple_operations":"true"}
            ```
            """);

        Assert.Equal(new BankingIntent("transfer", null, "savings", "chequing", "0.10", true), intent);
    }

    [Theory]
    [InlineData("I'm sorry, I can't help with that.")]
    [InlineData("{ not json }")]
    [InlineData("[1, 2, 3]")]
    public void Unreadable_output_is_reported_as_a_service_failure(string content) =>
        Assert.Throws<AiServiceException>(() => IntentInterpreter.Parse(content));
}
