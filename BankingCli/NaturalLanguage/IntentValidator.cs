using System.Globalization;
using System.Text.RegularExpressions;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// Checks what the model understood and turns it into exactly one simulator command,
/// or explains why nothing can run.
/// </summary>
/// <remarks>
/// Commands are built only from known values (the operation, the two account names, and a
/// formatted amount), never from the model's text. Accounts and amounts only count if the
/// customer actually wrote them.
/// </remarks>
internal static partial class IntentValidator
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

    private const string TooLargeMessage =
        "That amount is larger than the simulator can handle. Please try a smaller amount. " + NothingChanged;

    /// <summary>"Your request mentions more than one amount ($5.00 and $10.00)…"</summary>
    private static string WhichAmountMessage(string request)
    {
        List<string> amounts = StatedAmounts.AmountsIn(request)
            .Select(amount => "$" + amount.ToString("0.00", CultureInfo.InvariantCulture))
            .ToList();
        string list = amounts.Count <= 2
            ? string.Join(" and ", amounts)
            : $"{string.Join(", ", amounts[..^1])}, and {amounts[^1]}";

        return $"Your request mentions more than one amount ({list}). Please send it again with just the amount " +
               $"you want to use. {NothingChanged}";
    }

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
        AmountCheck amountCheck = CheckAmount(intent.Amount, request, out string amount);

        List<string> missing = new List<string>();
        if (amountCheck == AmountCheck.Missing)
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

        return RunOrExplain(amountCheck, $"{command} {account} {amount}", request);
    }

    /// <summary>
    /// Transfers need an amount and both accounts named by the customer.
    /// A missing side is asked for, never filled in, even though there are only two accounts.
    /// </summary>
    private static IntentDecision ValidateTransfer(BankingIntent intent, string request)
    {
        string? from = NamedAccount(intent.FromAccount, request);
        string? to = NamedAccount(intent.ToAccount, request);
        AmountCheck amountCheck = CheckAmount(intent.Amount, request, out string amount);

        List<string> missing = new List<string>();
        if (amountCheck == AmountCheck.Missing)
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
        return RunOrExplain(amountCheck, $"transfer {from} {to} {amount}", request);
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

    private enum AmountCheck
    {
        Missing,   // Not given, or the customer never wrote that number.
        Ambiguous, // The customer wrote more than one amount, such as "$5 or $10".
        Invalid,   // Not a positive amount with at most two decimal places.
        TooLarge,  // A proper amount, but too many digits to work with.
        Valid
    }

    /// <summary>Runs the command when the amount is valid, or explains what's wrong with the amount.</summary>
    private static IntentDecision RunOrExplain(AmountCheck amountCheck, string command, string request) => amountCheck switch
    {
        AmountCheck.Valid => IntentDecision.Run(command),
        AmountCheck.Ambiguous => IntentDecision.Decline(DecisionKind.MissingDetails, WhichAmountMessage(request)),
        AmountCheck.TooLarge => IntentDecision.Decline(DecisionKind.InvalidAmount, TooLargeMessage),
        _ => IntentDecision.Decline(DecisionKind.InvalidAmount, InvalidAmountMessage)
    };

    /// <summary>
    /// Checks the model's amount. It must be a number the customer actually wrote, above zero,
    /// with at most two decimal places. A valid amount is formatted the way the simulator expects, such as "4.00".
    /// </summary>
    private static AmountCheck CheckAmount(string? text, string request, out string formatted)
    {
        formatted = string.Empty;
        if (IsBlank(text))
        {
            return AmountCheck.Missing;
        }

        string trimmed = text!.Trim();
        if (trimmed.StartsWith('$'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (!decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal amount))
        {
            // Plain digits that won't fit in a number are a real amount, just far too large, as long as the
            // customer typed a number that size. (Models often miscopy long numbers, so the digits aren't compared.)
            if (IsPlainAmount(trimmed))
            {
                return StatedAmounts.HasNumberTooLargeToRead(request) ? AmountCheck.TooLarge : AmountCheck.Missing;
            }

            return AmountCheck.Invalid;
        }

        // A number the customer never wrote was made up by the model, so ask for the amount instead.
        IReadOnlyList<decimal> writtenAmounts = StatedAmounts.AmountsIn(request);
        if (!writtenAmounts.Contains(amount))
        {
            return AmountCheck.Missing;
        }

        // With more than one amount in the request ("$5 or $10"), don't let the model pick: ask.
        if (writtenAmounts.Count > 1)
        {
            return AmountCheck.Ambiguous;
        }

        // Decimal places are counted in the text, because reading a very long number can round it.
        if (amount <= 0m || DecimalPlaces(trimmed) > 2)
        {
            return AmountCheck.Invalid;
        }

        formatted = amount.ToString("0.00", CultureInfo.InvariantCulture);
        return AmountCheck.Valid;
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

    private static int DecimalPlaces(string number)
    {
        int point = number.IndexOf('.');
        return point < 0 ? 0 : number.Length - point - 1;
    }

    /// <summary>
    /// True for an amount written the way the simulator wants it: digits, above zero, with at most
    /// two decimal places and no sign, such as "25" or "25.50". It may still be too large.
    /// </summary>
    internal static bool IsPlainAmount(string text) => PlainAmountPattern().IsMatch(text);

    [GeneratedRegex(@"^(?=.*[1-9])\d+(?:\.\d{1,2})?$")]
    private static partial Regex PlainAmountPattern();

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
