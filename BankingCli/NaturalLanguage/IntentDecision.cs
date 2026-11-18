namespace BankingCli.NaturalLanguage;

/// <summary>The kinds of outcome when a request is validated.</summary>
internal enum DecisionKind
{
    Execute,            // Safe to run the command.
    MissingDetails,     // Ask the customer for an amount or account.
    InvalidAmount,      // The amount isn't positive with at most two decimal places.
    MultipleOperations, // The request asked for more than one thing.
    Unsupported         // Not a deposit, withdrawal, transfer, or balance check.
}

/// <summary>
/// The result of validating a request: either one simulator command to run,
/// or a message telling the customer why nothing was run.
/// </summary>
internal sealed record IntentDecision(DecisionKind Kind, string? Command, string Message)
{
    /// <summary>Run this one simulator command.</summary>
    public static IntentDecision Run(string command) =>
        new IntentDecision(DecisionKind.Execute, command, string.Empty);

    /// <summary>Run nothing, and show the customer this message.</summary>
    public static IntentDecision Decline(DecisionKind kind, string message) =>
        new IntentDecision(kind, null, message);
}
