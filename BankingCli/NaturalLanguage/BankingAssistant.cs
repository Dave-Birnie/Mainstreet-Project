namespace BankingCli.NaturalLanguage;

/// <summary>
/// Routes each line of input. Original commands go straight to the simulator. English requests are
/// interpreted by the API, validated locally, run as at most one simulator command, and then explained.
/// </summary>
internal sealed class BankingAssistant
{
    private readonly BankSession session;
    private readonly IntentInterpreter? interpreter;
    private readonly ReplyWriter? replyWriter;
    private readonly string? unavailableNotice;
    private readonly Action<string>? trace;

    /// <param name="chatClient">The API client, or null when natural language is not configured.</param>
    /// <param name="unavailableNotice">Shown after unrecognized input when <paramref name="chatClient"/> is null.</param>
    /// <param name="trace">Optional sink that shows the simulator command and raw result for English requests.</param>
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

    public async Task<CommandResult> HandleAsync(string input, CancellationToken cancellationToken = default)
    {
        bool isOriginalCommand = OriginalCommand.IsRecognized(input);
        if (isOriginalCommand || interpreter is null || replyWriter is null)
        {
            // Without an API, behave exactly like the original simulator.
            CommandResult original = session.Execute(input);
            return isOriginalCommand || unavailableNotice is null
                ? original
                : original with { Output = $"{original.Output}{Environment.NewLine}{unavailableNotice}" };
        }

        BankingIntent intent;
        try
        {
            intent = await interpreter.InterpretAsync(input, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new CommandResult(
                $"Sorry, I couldn't process that request because {Describe(ex)} Nothing has been changed. " +
                "You can try again, or use a command such as \"transfer savings chequing 4\" (type help for the list).");
        }

        IntentDecision decision = IntentValidator.Validate(intent, input);
        if (decision.Command is null)
        {
            return new CommandResult(decision.Message);
        }

        // The only place an English request reaches the balances: one validated command, run once.
        trace?.Invoke($"command: {decision.Command}");
        CommandResult result = session.Execute(decision.Command);
        trace?.Invoke($"result: {result.Output}");

        try
        {
            return new CommandResult(
                await replyWriter.WriteAsync(input, decision.Command, result.Output, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The command has already run. Show its real result and do not run it again.
            return new CommandResult(
                $"I couldn't write a conversational reply because {Describe(ex)} " +
                $"Here is the simulator's result:{Environment.NewLine}{result.Output}");
        }
    }

    private static string Describe(Exception ex) =>
        ex is AiServiceException ? ex.Message : $"of an unexpected error ({ex.Message}).";
}
