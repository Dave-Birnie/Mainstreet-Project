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
        BankingAssistant assistant = new BankingAssistant(new BankSession(), AiConnection.On(chat));

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

    #region Without an API key

    private static readonly AiConnection NoKey = AiConnection.Off("No API key is set.", "Set BANKING_AI_API_KEY.");

    [Fact]
    public async Task Without_an_API_piped_input_behaves_exactly_like_the_original_simulator()
    {
        BankingAssistant assistant = new BankingAssistant(new BankSession(), NoKey, interactive: false);

        CommandResult result = await assistant.HandleAsync("Put fifty dollars into savings");

        Assert.Equal(new BankSession().Execute("Put fifty dollars into savings"), result);
    }

    [Fact]
    public async Task Without_an_API_interactive_users_are_told_what_went_wrong_and_what_to_do()
    {
        BankingAssistant assistant = new BankingAssistant(new BankSession(), NoKey, interactive: true);

        CommandResult result = await assistant.HandleAsync("Put fifty dollars into savings");

        string[] lines = result.Output.Split(Environment.NewLine);
        Assert.Equal("ERROR UNKNOWN_COMMAND", lines[0]); // The simulator's own line is unchanged.
        Assert.Equal("I don't recognize that command. Type help to see the commands you can use.", lines[1]);
        Assert.Equal("Everyday-English requests are off, so I can only follow the original commands. " +
                     "Type status to see how to turn everyday English on.", lines[2]);
    }

    #endregion

    #region Interactive extras

    [Fact]
    public async Task Interactive_errors_keep_the_original_line_and_add_a_plain_explanation()
    {
        BankingAssistant assistant = new BankingAssistant(new BankSession(), NoKey, interactive: true);
        await assistant.HandleAsync("deposit chequing 3");

        CommandResult result = await assistant.HandleAsync("withdraw chequing 100");

        Assert.Equal(
            $"ERROR INSUFFICIENT_FUNDS | chequing=$3.00{Environment.NewLine}" +
            "Chequing only has $3.00, which isn't enough. Try a smaller amount, or move money into chequing first. " +
            "Nothing has been changed.",
            result.Output);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("?")]
    public async Task Interactive_help_keeps_the_original_list_and_adds_status_and_english_examples(string command)
    {
        FakeChatClient chat = new FakeChatClient();
        BankingAssistant assistant = new BankingAssistant(new BankSession(), AiConnection.On(chat), interactive: true);

        CommandResult result = await assistant.HandleAsync(command);

        Assert.StartsWith(new BankSession().Execute("help").Output + Environment.NewLine + "status", result.Output);
        Assert.Contains("Move $20 from savings to chequing", result.Output);
        Assert.Empty(chat.Calls);
    }

    [Fact]
    public async Task Interactive_help_without_an_API_says_how_to_turn_english_on()
    {
        BankingAssistant assistant = new BankingAssistant(new BankSession(), NoKey, interactive: true);

        CommandResult result = await assistant.HandleAsync("help");

        Assert.EndsWith("Everyday-English requests are off. Type status to see why, and how to turn them on.",
            result.Output);
    }

    #endregion
}
