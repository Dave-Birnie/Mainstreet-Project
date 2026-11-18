using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

/// <summary>Checks that original commands keep their exact behaviour and never call the API.</summary>
public class OriginalCommandTests
{
    // The README transcript plus help, ? and malformed commands that must keep the simulator's own errors.
    public static TheoryData<string> OriginalCommands =>
    [
        "deposit savings 10",
        "transfer savings chequing 4",
        "withdraw chequing 1",
        "balance",
        "balance savings",
        "BALANCE Chequing",
        "withdraw chequing 100",
        "transfer savings savings 1",
        "deposit savings -5",
        "deposit savings 1.234",
        "deposit savings abc",
        "help",
        "?",
        "",
        "exit"
    ];

    [Theory]
    [MemberData(nameof(OriginalCommands))]
    public async Task Original_commands_produce_identical_output_without_calling_the_API(string command)
    {
        BankSession reference = new BankSession();
        FakeChatClient chat = new FakeChatClient();
        BankingAssistant assistant = new BankingAssistant(new BankSession(), chat);

        // Replay the README setup first so balances are non-zero in both sessions.
        foreach (string setup in new[] { "deposit savings 10", "transfer savings chequing 4" })
        {
            reference.Execute(setup);
            await assistant.HandleAsync(setup);
        }

        CommandResult expected = reference.Execute(command);
        CommandResult actual = await assistant.HandleAsync(command);

        Assert.Equal(expected, actual);
        Assert.Empty(chat.Calls);
    }

    [Theory]
    [InlineData("Put fifty dollars into savings")]
    [InlineData("Withdraw $25 from chequing")]
    [InlineData("deposit 50 savings")]
    [InlineData("deposit chequing")]
    [InlineData("balance please")]
    [InlineData("help me move money")]
    [InlineData("transfer 4 from savings to chequing")]
    public void English_requests_are_not_treated_as_original_commands(string input) =>
        Assert.False(OriginalCommand.IsRecognized(input));

    [Fact]
    public async Task Without_an_API_the_app_behaves_exactly_like_the_original_simulator()
    {
        BankingAssistant assistant = new BankingAssistant(new BankSession(), chatClient: null);

        CommandResult result = await assistant.HandleAsync("Put fifty dollars into savings");

        Assert.Equal(new BankSession().Execute("Put fifty dollars into savings"), result);
    }

    [Fact]
    public async Task Without_an_API_interactive_users_are_told_why_english_did_not_work()
    {
        BankingAssistant assistant = new BankingAssistant(
            new BankSession(), chatClient: null, unavailableNotice: "Natural-language requests are off.");

        CommandResult result = await assistant.HandleAsync("Put fifty dollars into savings");

        Assert.Contains("ERROR UNKNOWN_COMMAND", result.Output);
        Assert.Contains("Natural-language requests are off.", result.Output);
    }
}
