namespace BankingCli.NaturalLanguage;

/// <summary>
/// Handles each line the user types.
/// </summary>
/// <remarks>
/// "status" and the original commands are handled directly. English requests go through these steps:
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
    private static readonly string NewLine = Environment.NewLine;

    #region Setup

    private readonly BankSession session;
    private readonly AiConnection connection;
    private readonly IntentInterpreter? interpreter;
    private readonly ReplyWriter? replyWriter;
    private readonly bool interactive;
    private readonly Action<string>? trace;

    // How the most recent AI call went, for the status command.
    private bool hasCalledAi;
    private AiServiceException? lastAiProblem;

    /// <param name="session">The original simulator. All balance changes go through it.</param>
    /// <param name="connection">Whether everyday-English requests are on, and how they're set up.</param>
    /// <param name="interactive">True when a person is typing. Adds plain-English explanations and extra help.
    /// When input is piped in, original commands print exactly what the starter printed.</param>
    /// <param name="trace">Optional. Shows the simulator command and raw result for English requests.</param>
    public BankingAssistant(
        BankSession session, AiConnection connection, bool interactive = false, Action<string>? trace = null)
    {
        this.session = session;
        this.connection = connection;
        this.interactive = interactive;
        this.trace = trace;

        if (connection.Client is not null)
        {
            interpreter = new IntentInterpreter(connection.Client);
            replyWriter = new ReplyWriter(connection.Client);
        }
    }

    #endregion

    #region Handling one line of input

    public async Task<CommandResult> HandleAsync(string input, CancellationToken cancellationToken = default)
    {
        // "status" works with or without the API, and never changes anything.
        if (input.Trim().Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(ConsoleText.Status(connection, hasCalledAi, lastAiProblem));
        }

        // Original commands go straight to the simulator, with no API call.
        if (OriginalCommand.IsRecognized(input))
        {
            return RunOriginalCommand(input);
        }

        // Without the API, everything else goes to the simulator too, like the original app.
        if (interpreter is null || replyWriter is null)
        {
            return RunWithoutApi(input);
        }

        // Step 1: ask the API what the customer wants. Nothing has run yet, so a failure here is safe.
        BankingIntent intent;
        try
        {
            intent = await interpreter.InterpretAsync(input, cancellationToken);
            RecordAiSuccess();
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            return new CommandResult(InterpretationFailedMessage(RecordAiFailure(ex)));
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
            RecordAiSuccess();
            return new CommandResult(reply);
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            // Step 5: the command has already run. Show its real result and don't run it again.
            return new CommandResult(ReplyFailedMessage(RecordAiFailure(ex), result.Output));
        }
    }

    #endregion

    #region Running simulator commands

    /// <summary>Runs an original command unchanged. Interactive users also get plain-English help with errors.</summary>
    private CommandResult RunOriginalCommand(string input)
    {
        CommandResult result = session.Execute(input);

        // Piped input gets exactly what the starter printed, so it can still be compared line by line.
        if (!interactive)
        {
            return result;
        }

        if (input.Trim().ToLowerInvariant() is "help" or "?")
        {
            return WithExtraLine(result, ConsoleText.MoreHelp(connection));
        }

        string? explanation = SimulatorErrors.Explain(result.Output);
        return explanation is null ? result : WithExtraLine(result, explanation);
    }

    /// <summary>Without the API, input that isn't an original command still goes to the simulator, as before.</summary>
    private CommandResult RunWithoutApi(string input)
    {
        CommandResult result = session.Execute(input);
        if (!interactive)
        {
            return result;
        }

        // Explain the error, and that everyday English is off.
        string? explanation = SimulatorErrors.Explain(result.Output);
        string englishOff = "Everyday-English requests are off, so I can only follow the original commands. " +
                            "Type status to see how to turn everyday English on.";
        return WithExtraLine(result, explanation is null ? englishOff : explanation + NewLine + englishOff);
    }

    /// <summary>Runs a validated command exactly once, showing it when --show-commands is on.</summary>
    private CommandResult RunOnce(string command)
    {
        trace?.Invoke($"command: {command}");
        CommandResult result = session.Execute(command);
        trace?.Invoke($"result: {result.Output}");
        return result;
    }

    private static CommandResult WithExtraLine(CommandResult result, string extra) =>
        result with { Output = result.Output.Length == 0 ? extra : result.Output + NewLine + extra };

    #endregion

    #region Tracking AI calls

    private void RecordAiSuccess()
    {
        hasCalledAi = true;
        lastAiProblem = null;
    }

    /// <summary>Remembers the failure for the status command. Unexpected errors are described the same way.</summary>
    private AiServiceException RecordAiFailure(Exception ex)
    {
        hasCalledAi = true;
        lastAiProblem = ex as AiServiceException ?? new AiServiceException(
            "something unexpected went wrong",
            "Try again, or use one of the commands (type help to see them).",
            details: ex.Message,
            ex);
        return lastAiProblem;
    }

    /// <summary>True when the caller cancelled. That's passed on rather than reported as a failure.</summary>
    private static bool IsCancellation(Exception ex, CancellationToken cancellationToken) =>
        ex is OperationCanceledException && cancellationToken.IsCancellationRequested;

    #endregion

    #region Messages

    /// <summary>Nothing ran. Says what went wrong, what to do, and that the commands still work.</summary>
    private static string InterpretationFailedMessage(AiServiceException problem) =>
        $"Sorry, I couldn't process that request: {problem.Problem}. Nothing has been changed.{NewLine}" +
        $"To fix this: {problem.Solution} You can still use commands such as \"transfer savings chequing 4\" " +
        "(type help for the list).";

    /// <summary>
    /// The command already ran. Shows its real result, explained in plain English if it was declined.
    /// There's no "try again" here, because trying again would run the command twice.
    /// </summary>
    private static string ReplyFailedMessage(AiServiceException problem, string simulatorResult)
    {
        string message = $"I ran your request, but couldn't write a conversational reply: {problem.Problem}. " +
                         $"Here is the simulator's result:{NewLine}{simulatorResult}";

        string? explanation = SimulatorErrors.Explain(simulatorResult);
        return explanation is null ? message : message + NewLine + explanation;
    }

    #endregion
}
