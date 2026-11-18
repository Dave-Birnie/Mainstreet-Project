using System.Globalization;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// Checks what the model understood and turns it into exactly one simulator command,
/// or explains why nothing can run.
/// </summary>
/// <remarks>
/// Commands are built only from known values (the operation, the two account names, and a
/// formatted amount), never from the model's text. An account only counts if the customer's
/// own words name it.
/// </remarks>
internal static class IntentValidator
{
    #region Messages shown to the customer

    private const string NothingChanged = "Nothing has been changed.";

    private const string MultipleOperationsMessage =
        "I can only handle one banking request at a time. Please send each request separately. " + NothingChanged;

    private const string UnsupportedMessage =
        "Sorry, I can only help with deposits, withdrawals, transfers between chequing and savings, " +
        "and balance checks. Try something like \"Move $20 from savings to chequing\", or type help " +
        "to see the commands.";

    private const string InvalidAmountMessage =
        "The amount needs to be a positive dollar value with no more than two decimal places, such as 25 or 25.50. " +
        "Please try again with a valid amount. " + NothingChanged;

    private const string WhichBalanceMessage =
        "Which balance would you like to see: chequing, savings, or both? Please include it when you try again.";

    #endregion

    #region Entry point

    /// <summary>Decides what to do with the model's reading of a request.</summary>
    /// <param name="intent">What the model understood.</param>
    /// <param name="request">The customer's original words, used to confirm which accounts they named.</param>
    public static IntentDecision Validate(BankingIntent intent, string request)
    {
        if (intent.MultipleOperations)
        {
            return IntentDecision.Decline(DecisionKind.MultipleOperations, MultipleOperationsMessage);
        }

        return intent.Operation?.Trim().ToLowerInvariant() switch
        {
            "deposit" => ValidateDeposit(intent, request),
            "withdraw" => ValidateWithdrawal(intent, request),
            "transfer" => ValidateTransfer(intent, request),
            "balance" => ValidateBalance(intent, request),
            _ => IntentDecision.Decline(DecisionKind.Unsupported, UnsupportedMessage)
        };
    }

    #endregion

    #region Checks for each operation

    private static IntentDecision ValidateDeposit(BankingIntent intent, string request) =>
        ValidateSingleAccount("deposit", intent, request,
            action: "make that deposit",
            accountDetail: "the account to deposit into (chequing or savings)",
            example: "Deposit $50 into savings");

    private static IntentDecision ValidateWithdrawal(BankingIntent intent, string request) =>
        ValidateSingleAccount("withdraw", intent, request,
            action: "make that withdrawal",
            accountDetail: "the account to withdraw from (chequing or savings)",
            example: "Withdraw $20 from chequing");

    /// <summary>Deposits and withdrawals need an amount and one account named by the customer.</summary>
    private static IntentDecision ValidateSingleAccount(
        string command, BankingIntent intent, string request, string action, string accountDetail, string example)
    {
        string? account = NamedAccount(intent.Account, request);

        List<string> missing = new List<string>();
        if (IsBlank(intent.Amount))
        {
            missing.Add("the amount");
        }

        if (account is null)
        {
            missing.Add(accountDetail);
        }

        if (missing.Count > 0)
        {
            return AskFor(action, missing, example);
        }

        return TryFormatAmount(intent.Amount!, out string amount)
            ? IntentDecision.Run($"{command} {account} {amount}")
            : IntentDecision.Decline(DecisionKind.InvalidAmount, InvalidAmountMessage);
    }

    /// <summary>
    /// Transfers need an amount and both accounts named by the customer.
    /// A missing side is asked for, never filled in, even though there are only two accounts.
    /// </summary>
    private static IntentDecision ValidateTransfer(BankingIntent intent, string request)
    {
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

        // "Savings to savings" is passed through, so the simulator applies its own SAME_ACCOUNT rule.
        return TryFormatAmount(intent.Amount!, out string amount)
            ? IntentDecision.Run($"transfer {from} {to} {amount}")
            : IntentDecision.Decline(DecisionKind.InvalidAmount, InvalidAmountMessage);
    }

    /// <summary>
    /// Balance checks are read-only, so if no account is named, both balances are shown,
    /// like the original "balance" command.
    /// </summary>
    private static IntentDecision ValidateBalance(BankingIntent intent, string request)
    {
        string? requested = intent.Account?.Trim().ToLowerInvariant();
        if (requested is null or "" or "both" or "all")
        {
            return IntentDecision.Run("balance");
        }

        string? account = Accounts.Normalize(requested);
        if (account is null)
        {
            return IntentDecision.Decline(DecisionKind.MissingDetails, WhichBalanceMessage);
        }

        // If the customer didn't name the account, the model picked it, so show both instead.
        return IntentDecision.Run(Accounts.IsNamedIn(request, account) ? $"balance {account}" : "balance");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Accepts a positive amount with no more than two decimal places and formats it the way the
    /// simulator expects, such as "4.00". Anything else (negative, zero, words, symbols) is rejected.
    /// </summary>
    internal static bool TryFormatAmount(string text, out string formatted)
    {
        formatted = string.Empty;

        string trimmed = text.Trim();
        if (trimmed.StartsWith('$'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        bool isNumber = decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out decimal amount);

        if (!isNumber || amount <= 0m || amount != decimal.Round(amount, 2))
        {
            return false;
        }

        formatted = amount.ToString("0.00", CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>
    /// The model's account, but only if the customer actually named it.
    /// Otherwise the model supplied it on its own, so it's treated as missing and the customer is asked.
    /// </summary>
    private static string? NamedAccount(string? modelAccount, string request)
    {
        string? account = Accounts.Normalize(modelAccount);
        return account is not null && Accounts.IsNamedIn(request, account) ? account : null;
    }

    private static bool IsBlank(string? text) => string.IsNullOrWhiteSpace(text);

    /// <summary>Builds a message such as "I need a bit more information to make that transfer: the amount."</summary>
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

    #endregion
}
