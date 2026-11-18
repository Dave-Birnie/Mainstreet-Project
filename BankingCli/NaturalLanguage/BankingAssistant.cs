namespace BankingCli.NaturalLanguage;

/// <summary>
/// Handles each line the user types.
/// </summary>
/// <remarks>
/// Original commands go straight to the simulator. English requests go through these steps:
/// <list type="number">
///   <item>Ask the API what the customer wants.</item>
///   <item>Check that answer locally.</item>
///   <item>Run exactly one simulator command.</item>
///   <item>Ask the API to explain the real result.</item>
///   <item>If that fails, show the simulator's own result instead. The command is never run again.</item>
/// </list>
/// </remarks>
internal sealed class BankingAssistant
{
    #region Setup

    private readonly BankSession session;
    private readonly IntentInterpreter? interpreter;
    private readonly ReplyWriter? replyWriter;
    private readonly string? unavailableNotice;
    private readonly Action<string>? trace;

    /// <param name="session">The original simulator. All balance changes go through it.</param>
    /// <param name="chatClient">The API client, or null when no API key is configured.</param>
    /// <param name="unavailableNotice">Added after unrecognized input when there is no API client.</param>
    /// <param name="trace">Optional. Shows the simulator command and raw result for English requests.</param>
    public BankingAssistant(
        BankSession session, IChatClient? chatClient, string? unavailableNotice = null, Action<string>? trace = null)
    {
        this.session = session;
        this.unavailableNotice = unavailableNotice;
        this.trace = trace;

        if (chatClient is not null)
        {
            interpreter = new IntentInterpreter(chatClient);
            replyWriter = new ReplyWriter(chatClient);
        }
    }

    #endregion

    #region Handling one line of input

    public async Task<CommandResult> HandleAsync(string input, CancellationToken cancellationToken = default)
    {
        // Original commands go straight to the simulator. So does everything when there's no API.
        bool isOriginalCommand = OriginalCommand.IsRecognized(input);
        if (isOriginalCommand || interpreter is null || replyWriter is null)
        {
            return RunWithoutApi(input, isOriginalCommand);
        }

        // Step 1: ask the API what the customer wants. Nothing has run yet, so a failure here is safe.
        BankingIntent intent;
        try
        {
            intent = await interpreter.InterpretAsync(input, cancellationToken);
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            return new CommandResult(InterpretationFailedMessage(ex));
        }

        // Step 2: check it locally. If anything is missing or invalid, explain why and run nothing.
        IntentDecision decision = IntentValidator.Validate(intent, input);
        if (decision.Command is null)
        {
            return new CommandResult(decision.Message);
        }

        // Step 3: run the one validated command. This is the only place an English request changes a balance.
        CommandResult result = RunOnce(decision.Command);

        // Step 4: ask the API to explain the real result.
        try
        {
            string reply = await replyWriter.WriteAsync(input, decision.Command, result.Output, cancellationToken);
            return new CommandResult(reply);
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            // Step 5: the command has already run. Show its real result and don't run it again.
            return new CommandResult(ReplyFailedMessage(ex, result.Output));
        }
    }

    #endregion

    #region Running simulator commands

    /// <summary>Runs the input on the simulator unchanged, just like the original app.</summary>
    private CommandResult RunWithoutApi(string input, bool isOriginalCommand)
    {
        CommandResult result = session.Execute(input);

        // Interactive users who typed English without an API key are told why it didn't work.
        if (!isOriginalCommand && unavailableNotice is not null)
        {
            return result with { Output = $"{result.Output}{Environment.NewLine}{unavailableNotice}" };
        }

        return result;
    }

    /// <summary>Runs a validated command exactly once, showing it when --show-commands is on.</summary>
    private CommandResult RunOnce(string command)
    {
        trace?.Invoke($"command: {command}");
        CommandResult result = session.Execute(command);
        trace?.Invoke($"result: {result.Output}");
        return result;
    }

    #endregion

    #region Messages

    private static string InterpretationFailedMessage(Exception ex) =>
        $"Sorry, I couldn't process that request because {Describe(ex)} Nothing has been changed. " +
        "You can try again, or use a command such as \"transfer savings chequing 4\" (type help for the list).";

    private static string ReplyFailedMessage(Exception ex, string simulatorResult) =>
        $"I couldn't write a conversational reply because {Describe(ex)} " +
        $"Here is the simulator's result:{Environment.NewLine}{simulatorResult}";

    /// <summary>The reason for a failure, written to follow "because".</summary>
    private static string Describe(Exception ex) =>
        ex is AiServiceException ? ex.Message : $"of an unexpected error ({ex.Message}).";

    /// <summary>True when the caller cancelled. That's passed on rather than reported as a failure.</summary>
    private static bool IsCancellation(Exception ex, CancellationToken cancellationToken) =>
        ex is OperationCanceledException && cancellationToken.IsCancellationRequested;

    #endregion
}
