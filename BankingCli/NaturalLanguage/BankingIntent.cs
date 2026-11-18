namespace BankingCli.NaturalLanguage;

/// <summary>
/// What the model understood from one request, as returned in its JSON.
/// Every field is untrusted text until <see cref="IntentValidator"/> has checked it.
/// </summary>
/// <param name="Operation">"deposit", "withdraw", "transfer", "balance", or "unsupported".</param>
/// <param name="Account">The account for a deposit, withdrawal, or balance check.</param>
/// <param name="FromAccount">The account a transfer takes money out of.</param>
/// <param name="ToAccount">The account a transfer puts money into.</param>
/// <param name="Amount">The dollar amount as text, such as "50" or "25.75".</param>
/// <param name="MultipleOperations">True when the request asked for more than one thing.</param>
internal sealed record BankingIntent(
    string? Operation,
    string? Account,
    string? FromAccount,
    string? ToAccount,
    string? Amount,
    bool MultipleOperations);
