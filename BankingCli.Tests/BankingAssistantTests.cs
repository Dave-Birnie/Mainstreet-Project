using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

/// <summary>End-to-end flow through the real simulator, with a scripted fake standing in for the AI service.</summary>
public class BankingAssistantTests
{
    private readonly BankSession session = new BankSession();
    private readonly FakeChatClient chat = new FakeChatClient();
    private readonly List<string> trace = [];

    private BankingAssistant CreateAssistant() => new BankingAssistant(session, AiConnection.On(chat), trace: trace.Add);

    private string Balances() => session.Execute("balance").Output;

    #region The three required checks

    [Fact]
    public async Task Successful_transfer_runs_the_command_once_and_returns_the_conversational_reply()
    {
        BankingAssistant assistant = CreateAssistant();
        await assistant.HandleAsync("deposit savings 10");
        const string reply = "All set! I moved $4.00 from savings to chequing. You now have $6.00 in savings and $4.00 in chequing.";
        chat.ReturnsIntent("transfer", from: "savings", to: "chequing", amount: "4").Returns(reply);

        CommandResult result = await assistant.HandleAsync("Move four dollars from savings to chequing");

        Assert.Equal(reply, result.Output);
        Assert.Equal(
            ["command: transfer savings chequing 4.00",
             "result: TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00"],
            trace);
        Assert.Equal("chequing=$4.00 | savings=$6.00", Balances());
        Assert.Contains("Simulator result: TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00",
            chat.Calls[1].Messages[1].Content);
    }

    [Fact]
    public async Task Missing_amount_asks_for_clarification_and_changes_nothing()
    {
        BankingAssistant assistant = CreateAssistant();
        await assistant.HandleAsync("deposit savings 10");
        chat.ReturnsIntent("transfer", from: "savings", to: "chequing");

        CommandResult result = await assistant.HandleAsync("Move some money from savings to chequing");

        Assert.Contains("I need a bit more information to make that transfer: the amount.", result.Output);
        Assert.Equal("chequing=$0.00 | savings=$10.00", Balances());
        Assert.Single(chat.Calls); // No reply call, because nothing ran.
        Assert.Empty(trace);
    }

    [Fact]
    public async Task Declined_withdrawal_is_explained_and_balances_are_unchanged()
    {
        BankingAssistant assistant = CreateAssistant();
        await assistant.HandleAsync("deposit chequing 10");
        const string reply = "Sorry, I couldn't withdraw $25.00 because chequing only has $10.00. Your balance hasn't changed.";
        chat.ReturnsIntent("withdraw", "chequing", amount: "25").Returns(reply);

        CommandResult result = await assistant.HandleAsync("Withdraw $25 from chequing");

        Assert.Equal(reply, result.Output);
        Assert.Equal("result: ERROR INSUFFICIENT_FUNDS | chequing=$10.00", trace[1]);
        Assert.Equal("chequing=$10.00 | savings=$0.00", Balances());
    }

    #endregion

    #region API failures never run a command twice

    [Fact]
    public async Task Interpretation_failure_runs_nothing_and_points_to_the_original_commands()
    {
        BankingAssistant assistant = CreateAssistant();
        await assistant.HandleAsync("deposit savings 10");
        chat.Throws(new AiServiceException("the AI service took longer than 30 seconds to answer", "Try again in a moment."));

        CommandResult result = await assistant.HandleAsync("Move four dollars from savings to chequing");

        Assert.Equal(
            "Sorry, I couldn't process that request: the AI service took longer than 30 seconds to answer. " +
            $"Nothing has been changed.{Environment.NewLine}" +
            "To fix this: Try again in a moment. You can still use commands such as \"transfer savings chequing 4\" " +
            "(type help for the list).",
            result.Output);
        Assert.Equal("chequing=$0.00 | savings=$10.00", Balances());
        Assert.Single(chat.Calls);
    }

    [Fact]
    public async Task Reply_failure_shows_the_original_result_and_does_not_run_the_command_again()
    {
        BankingAssistant assistant = CreateAssistant();
        chat.ReturnsIntent("deposit", "savings", amount: "10")
            .Throws(new AiServiceException("the AI service had a problem on its end", "Wait a moment and try again."));

        CommandResult result = await assistant.HandleAsync("Put ten dollars in savings");

        // No "try again" here: the deposit already happened, so trying again would deposit twice.
        Assert.Equal(
            "I ran your request, but couldn't write a conversational reply: the AI service had a problem on its end. " +
            $"Here is the simulator's result:{Environment.NewLine}DEPOSIT $10.00 TO savings | savings=$10.00",
            result.Output);
        Assert.Equal("chequing=$0.00 | savings=$10.00", Balances()); // Deposited once, not twice.
        Assert.Equal(2, chat.Calls.Count); // One interpretation, one reply attempt, no retries.
    }

    [Fact]
    public async Task Reply_with_a_misreported_balance_falls_back_to_the_original_result()
    {
        BankingAssistant assistant = CreateAssistant();
        chat.ReturnsIntent("deposit", "savings", amount: "10").Returns("Done! You now have $100.00 in savings.");

        CommandResult result = await assistant.HandleAsync("Put ten dollars in savings");

        Assert.EndsWith($"{Environment.NewLine}DEPOSIT $10.00 TO savings | savings=$10.00", result.Output);
        Assert.Equal("chequing=$0.00 | savings=$10.00", Balances());
    }

    [Fact]
    public async Task Reply_claiming_success_for_a_declined_withdrawal_falls_back_to_the_explained_result()
    {
        BankingAssistant assistant = CreateAssistant();
        await assistant.HandleAsync("deposit chequing 10");
        chat.ReturnsIntent("withdraw", "chequing", amount: "25").Returns("Done! I withdrew $25.00 from chequing.");

        CommandResult result = await assistant.HandleAsync("Withdraw $25 from chequing");

        Assert.Contains("(it didn't say the request was declined)", result.Output);
        Assert.Contains($"{Environment.NewLine}ERROR INSUFFICIENT_FUNDS | chequing=$10.00{Environment.NewLine}", result.Output);
        Assert.EndsWith("Chequing only has $10.00, which isn't enough. Try a smaller amount, or move money into " +
                        "chequing first. Nothing has been changed.", result.Output);
        Assert.Equal("chequing=$10.00 | savings=$0.00", Balances());
    }

    [Fact]
    public async Task Unexpected_reply_errors_still_show_the_original_result()
    {
        BankingAssistant assistant = CreateAssistant();
        chat.ReturnsIntent("deposit", "savings", amount: "10").Throws(new InvalidOperationException("boom"));

        CommandResult result = await assistant.HandleAsync("Put ten dollars in savings");

        Assert.EndsWith("DEPOSIT $10.00 TO savings | savings=$10.00", result.Output);
        Assert.Equal("chequing=$0.00 | savings=$10.00", Balances());
    }

    #endregion

    #region Requests that must not run anything

    [Fact]
    public async Task Multiple_operations_in_one_request_run_nothing()
    {
        BankingAssistant assistant = CreateAssistant();
        chat.ReturnsIntent("deposit", "savings", amount: "50", multiple: true);

        CommandResult result = await assistant.HandleAsync("Deposit $50 into savings and then move $20 to chequing");

        Assert.Contains("one banking request at a time", result.Output);
        Assert.Equal("chequing=$0.00 | savings=$0.00", Balances());
        Assert.Single(chat.Calls);
    }

    // From the ChatGPT review: if the model supplies an amount the customer never gave, it must not run.
    [Fact]
    public async Task An_amount_the_customer_never_gave_is_asked_for_instead_of_run()
    {
        BankingAssistant assistant = CreateAssistant();
        await assistant.HandleAsync("deposit savings 50");
        chat.ReturnsIntent("transfer", from: "savings", to: "chequing", amount: "10");

        CommandResult result = await assistant.HandleAsync("Move some money from savings to chequing");

        Assert.Contains("I need a bit more information to make that transfer: the amount.", result.Output);
        Assert.Equal("chequing=$0.00 | savings=$50.00", Balances());
        Assert.Empty(trace);
    }

    [Theory]
    [InlineData("-50")]
    [InlineData("12.345")]
    public async Task Negative_or_malformed_amounts_run_nothing(string amount)
    {
        BankingAssistant assistant = CreateAssistant();
        chat.ReturnsIntent("deposit", "savings", amount: amount);

        CommandResult result = await assistant.HandleAsync($"Deposit {amount} dollars into savings");

        Assert.Contains("positive dollar value with no more than two decimal places", result.Output);
        Assert.Equal("chequing=$0.00 | savings=$0.00", Balances());
    }

    [Fact]
    public async Task Unsupported_request_runs_nothing()
    {
        BankingAssistant assistant = CreateAssistant();
        chat.ReturnsIntent("unsupported");

        CommandResult result = await assistant.HandleAsync("Open a new TFSA for me");

        Assert.StartsWith("Sorry, I can only help with deposits, withdrawals, transfers", result.Output);
        Assert.Single(chat.Calls);
    }

    #endregion

    #region Status command

    [Fact]
    public async Task Status_shows_the_setup_and_how_the_last_AI_call_went()
    {
        AiSettings settings = new AiSettings(
            new Uri("https://llm.example.test/v1/chat/completions"), "test-model", "secret-key", TimeSpan.FromSeconds(30));
        BankingAssistant assistant = new BankingAssistant(session, AiConnection.On(chat, settings));

        string beforeAnyCalls = (await assistant.HandleAsync("status")).Output;

        chat.Throws(new AiServiceException("the AI service didn't accept the API key", "Check the key.", "HTTP 401"));
        await assistant.HandleAsync("Move four dollars from savings to chequing");
        string afterAFailure = (await assistant.HandleAsync("STATUS")).Output;

        Assert.Contains("Everyday-English requests: on", beforeAnyCalls);
        Assert.Contains("Model:     test-model", beforeAnyCalls);
        Assert.Contains("API key:   from BANKING_AI_API_KEY", beforeAnyCalls);
        Assert.Contains("Last AI request: none yet", beforeAnyCalls);
        Assert.DoesNotContain("secret-key", beforeAnyCalls);

        Assert.Contains("Last AI request: failed because the AI service didn't accept the API key.", afterAFailure);
        Assert.Contains("To fix:  Check the key.", afterAFailure);
        Assert.Contains("Details: HTTP 401", afterAFailure);
        Assert.Single(chat.Calls); // "status" itself never calls the API.
    }

    [Fact]
    public async Task Status_without_an_API_says_why_and_how_to_fix_it()
    {
        BankingAssistant assistant = new BankingAssistant(
            session, AiConnection.Off("No API key is set.", "Restart the app and enter a key when asked."));

        string status = (await assistant.HandleAsync("status")).Output;

        Assert.Equal(
            $"Everyday-English requests: off{Environment.NewLine}" +
            $"  Why:     No API key is set.{Environment.NewLine}" +
            $"  To fix:  Restart the app and enter a key when asked.{Environment.NewLine}" +
            "Original commands work either way. Type help to see them.",
            status);
    }

    #endregion

    #region Balance checks

    [Fact]
    public async Task Balance_inquiry_is_read_only()
    {
        BankingAssistant assistant = CreateAssistant();
        await assistant.HandleAsync("deposit savings 6");
        await assistant.HandleAsync("deposit chequing 4");
        chat.ReturnsIntent("balance", "both").Returns("You have $4.00 in chequing and $6.00 in savings.");

        CommandResult result = await assistant.HandleAsync("How much money do I have in both accounts?");

        Assert.Equal("You have $4.00 in chequing and $6.00 in savings.", result.Output);
        Assert.Equal("command: balance", trace[0]);
        Assert.Equal("chequing=$4.00 | savings=$6.00", Balances());
    }

    #endregion
}
