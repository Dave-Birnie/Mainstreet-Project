using System.Text.RegularExpressions;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// The simulator's error codes: what each one means, and what the user can do about it.
/// </summary>
internal static partial class SimulatorErrors
{
    private const string NothingChanged = "Nothing has been changed.";

    #region The error codes

    /// <param name="Meaning">A factual description, given to the model so it never guesses why a request was declined.</param>
    /// <param name="Problem">A short, plain description for the user.</param>
    /// <param name="Fix">What the user can do about it.</param>
    /// <param name="ChangedNothing">True for declined transactions, where it helps to say the balances are untouched.</param>
    private sealed record ErrorInfo(string Meaning, string Problem, string Fix, bool ChangedNothing);

    private static readonly Dictionary<string, ErrorInfo> Errors = new Dictionary<string, ErrorInfo>
    {
        ["INSUFFICIENT_FUNDS"] = new ErrorInfo(
            Meaning: "The account the money would come from does not have enough funds. " +
                     "The balance shown is that account's current balance.",
            Problem: "There isn't enough money in that account.",
            Fix: "Try a smaller amount, or move money into that account first.",
            ChangedNothing: true),

        ["SAME_ACCOUNT"] = new ErrorInfo(
            Meaning: "A transfer must be between two different accounts.",
            Problem: "A transfer has to be between two different accounts.",
            Fix: "Name two different accounts, such as \"transfer savings chequing 5\".",
            ChangedNothing: true),

        ["INVALID_AMOUNT"] = new ErrorInfo(
            Meaning: "The amount was not accepted. Amounts must be positive, have at most two decimal " +
                     "places, and be within the simulator's limit.",
            Problem: "That amount isn't valid.",
            Fix: "Use a number above zero with at most two decimal places, such as 5 or 5.25, without a $ sign.",
            ChangedNothing: true),

        ["BALANCE_LIMIT"] = new ErrorInfo(
            Meaning: "The transaction would take the receiving account above the simulator's maximum balance.",
            Problem: "That would take the account above the simulator's maximum balance.",
            Fix: "Try a smaller amount.",
            ChangedNothing: true),

        ["INVALID_ACCOUNT"] = new ErrorInfo(
            Meaning: "The account name was not recognized. The accounts are chequing and savings.",
            Problem: "That account name isn't recognized.",
            Fix: "The accounts are chequing and savings.",
            ChangedNothing: true),

        ["INVALID_ARGUMENTS"] = new ErrorInfo(
            Meaning: "The command was not in the expected format.",
            Problem: "That command has missing or extra words.",
            Fix: "Type help to see the format of each command, such as \"deposit savings 10\".",
            ChangedNothing: false),

        ["UNKNOWN_COMMAND"] = new ErrorInfo(
            Meaning: "The command was not recognized.",
            Problem: "I don't recognize that command.",
            Fix: "Type help to see the commands you can use.",
            ChangedNothing: false)
    };

    #endregion

    #region Reading results

    /// <summary>Returns the code from a result such as "ERROR INSUFFICIENT_FUNDS | chequing=$0.00", or null on success.</summary>
    public static string? Code(string simulatorResult)
    {
        if (!simulatorResult.StartsWith("ERROR ", StringComparison.Ordinal))
        {
            return null;
        }

        return simulatorResult["ERROR ".Length..].Split(' ', '|')[0];
    }

    /// <summary>The factual meaning of an error code, for the model.</summary>
    public static string MeaningFor(string code) =>
        Errors.TryGetValue(code, out ErrorInfo? info) ? info.Meaning : "The simulator declined the request.";

    #endregion

    #region Explaining errors to the user

    /// <summary>
    /// A plain-English explanation of a simulator error and what to do about it,
    /// or null when the result isn't an error.
    /// </summary>
    public static string? Explain(string simulatorResult)
    {
        string? code = Code(simulatorResult);
        if (code is null)
        {
            return null;
        }

        if (!Errors.TryGetValue(code, out ErrorInfo? info))
        {
            return $"The simulator declined that request. {NothingChanged}";
        }

        // For insufficient funds, use the real balance the simulator reported.
        string problem = info.Problem;
        string fix = info.Fix;
        Match balance = ReportedBalance().Match(simulatorResult);
        if (code == "INSUFFICIENT_FUNDS" && balance.Success)
        {
            string account = balance.Groups["account"].Value;
            problem = $"{char.ToUpperInvariant(account[0])}{account[1..]} only has {balance.Groups["amount"].Value}, " +
                      "which isn't enough.";
            fix = $"Try a smaller amount, or move money into {account} first.";
        }

        return info.ChangedNothing ? $"{problem} {fix} {NothingChanged}" : $"{problem} {fix}";
    }

    [GeneratedRegex(@"\| (?<account>chequing|savings)=(?<amount>\$\d+\.\d{2})")]
    private static partial Regex ReportedBalance();

    #endregion
}
