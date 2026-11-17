using System.Globalization;
using System.Text.RegularExpressions;

namespace BankingCli.NaturalLanguage;

internal enum DecisionKind
{
    Execute,
    MissingDetails,
    InvalidAmount,
    MultipleOperations,
    Unsupported
}

/// <summary>
/// The result of validating an intent: either exactly one simulator command, or a message
/// for the user explaining why nothing was run.
/// </summary>
internal sealed record IntentDecision(DecisionKind Kind, string? Command, string Message)
{
    public static IntentDecision Run(string command) => new IntentDecision(DecisionKind.Execute, command, string.Empty);

    public static IntentDecision Decline(DecisionKind kind, string message) => new IntentDecision(kind, null, message);
}

/// <summary>
/// Checks the model's output locally and builds the simulator command from known values only.
/// No text from the model is ever copied into the command, and an account only counts if the
/// customer's own words name it.
/// </summary>
internal static partial class IntentValidator
{
    private const string Chequing = "chequing";
    private const string Savings = "savings";
    private const string NothingChanged = "Nothing has been changed.";

    /// <param name="intent">The model's reading of the request.</param>
    /// <param name="request">The customer's original text, used to confirm that named accounts were really said.</param>
    public static IntentDecision Validate(BankingIntent intent, string request)
    {
        if (intent.MultipleOperations)
        {
            return IntentDecision.Decline(DecisionKind.MultipleOperations,
                "I can only handle one banking request at a time. Please send each request separately. " +
                NothingChanged);
        }

        return intent.Operation?.Trim().ToLowerInvariant() switch
        {
            "balance" => ValidateBalance(intent, request),
            "deposit" => ValidateSingleAccount("deposit", intent, request,
                "make that deposit", "the account to deposit into (chequing or savings)", "Deposit $50 into savings"),
            "withdraw" => ValidateSingleAccount("withdraw", intent, request,
                "make that withdrawal", "the account to withdraw from (chequing or savings)", "Withdraw $20 from chequing"),
            "transfer" => ValidateTransfer(intent, request),
            _ => IntentDecision.Decline(DecisionKind.Unsupported,
                "Sorry, I can only help with deposits, withdrawals, transfers between chequing and savings, " +
                "and balance checks. Try something like \"Move $20 from savings to chequing\", or type help " +
                "to see the commands.")
        };
    }

    private static IntentDecision ValidateBalance(BankingIntent intent, string request)
    {
        string? requested = intent.Account?.Trim().ToLowerInvariant();

        // A balance inquiry is read-only, so an unnamed account safely means both, like the original "balance".
        if (requested is null or "" or "both" or "all")
        {
            return IntentDecision.Run("balance");
        }

        string? account = NormalizeAccount(requested);
        if (account is null)
        {
            return IntentDecision.Decline(DecisionKind.MissingDetails,
                "Which balance would you like to see: chequing, savings, or both? Please include it when you try again.");
        }

        return IntentDecision.Run(IsNamedIn(request, account) ? $"balance {account}" : "balance");
    }

    private static IntentDecision ValidateSingleAccount(
        string command, BankingIntent intent, string request, string action, string accountDescription, string example)
    {
        string? account = NamedAccount(intent.Account, request);

        List<string> missing = new List<string>();
        if (IsBlank(intent.Amount))
        {
            missing.Add("the amount");
        }

        if (account is null)
        {
            missing.Add(accountDescription);
        }

        if (missing.Count > 0)
        {
            return AskFor(action, missing, example);
        }

        return TryFormatAmount(intent.Amount!, out string amount)
            ? IntentDecision.Run($"{command} {account} {amount}")
            : InvalidAmount();
    }

    private static IntentDecision ValidateTransfer(BankingIntent intent, string request)
    {
        // Both sides must be named by the customer. A missing side is asked for, never filled in,
        // even though there are only two accounts.
        string? from = NamedAccount(intent.FromAccount, request);
        string? to = NamedAccount(intent.ToAccount, request);

        List<string> missing = new List<string>();
        if (IsBlank(intent.Amount))
        {
            missing.Add("the amount");
        }

        if (from is null)
        {
            missing.Add("the account to move money from (chequing or savings)");
        }

        if (to is null)
        {
            missing.Add("the account to move money to (chequing or savings)");
        }

        if (missing.Count > 0)
        {
            return AskFor("make that transfer", missing, "Move $20 from savings to chequing");
        }

        // A transfer to the same account is passed through so the simulator applies its own SAME_ACCOUNT rule.
        return TryFormatAmount(intent.Amount!, out string amount)
            ? IntentDecision.Run($"transfer {from} {to} {amount}")
            : InvalidAmount();
    }

    /// <summary>
    /// Accepts a positive amount with no more than two decimal places and formats it the way the
    /// simulator expects. Anything else (negative, zero, words, symbols) is rejected.
    /// </summary>
    internal static bool TryFormatAmount(string text, out string formatted)
    {
        formatted = string.Empty;
        string trimmed = text.Trim();
        if (trimmed.StartsWith('$'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (!decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal amount)
            || amount <= 0m
            || amount != decimal.Round(amount, 2))
        {
            return false;
        }

        formatted = amount.ToString("0.00", CultureInfo.InvariantCulture);
        return true;
    }

    private static string? NormalizeAccount(string? account) => account?.Trim().ToLowerInvariant() switch
    {
        "chequing" or "checking" => Chequing,
        "savings" or "saving" => Savings,
        _ => null
    };

    /// <summary>
    /// The model's account, but only if the customer's own words name it. Otherwise the model
    /// supplied it on its own, so it is treated as missing and the customer is asked.
    /// </summary>
    private static string? NamedAccount(string? modelAccount, string request)
    {
        string? account = NormalizeAccount(modelAccount);
        return account is not null && IsNamedIn(request, account) ? account : null;
    }

    private static bool IsBlank(string? text) => string.IsNullOrWhiteSpace(text);

    /// <summary>
    /// True when the customer's text contains a word for the account. A typo or nickname fails this
    /// check and leads to a clarification question, which is the safe direction to be wrong in.
    /// </summary>
    internal static bool IsNamedIn(string request, string account) => account == Chequing
        ? ChequingWords().IsMatch(request)
        : SavingsWords().IsMatch(request);

    [GeneratedRegex(@"\b(chequing|chequings|checking|checkings|cheq|chq)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChequingWords();

    [GeneratedRegex(@"\b(savings?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SavingsWords();

    private static IntentDecision AskFor(string action, List<string> missing, string example)
    {
        string details = missing.Count switch
        {
            1 => missing[0],
            2 => $"{missing[0]} and {missing[1]}",
            _ => $"{string.Join(", ", missing[..^1])}, and {missing[^1]}"
        };
        string pronoun = missing.Count == 1 ? "it" : "them";

        return IntentDecision.Decline(DecisionKind.MissingDetails,
            $"I need a bit more information to {action}: {details}. Please include {pronoun} when you try " +
            $"again, for example \"{example}\". {NothingChanged}");
    }

    private static IntentDecision InvalidAmount() => IntentDecision.Decline(DecisionKind.InvalidAmount,
        "The amount needs to be a positive dollar value with no more than two decimal places, such as 25 or 25.50. " +
        $"Please try again with a valid amount. {NothingChanged}");
}
