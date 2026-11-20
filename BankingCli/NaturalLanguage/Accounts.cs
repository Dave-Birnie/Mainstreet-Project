using System.Text.RegularExpressions;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// The simulator's two account names, and the words a customer might use for them.
/// </summary>
internal static partial class Accounts
{
    public const string Chequing = "chequing";
    public const string Savings = "savings";

    /// <summary>True for the exact names the simulator accepts, in any letter case.</summary>
    public static bool IsSimulatorName(string word) =>
        word.Equals(Chequing, StringComparison.OrdinalIgnoreCase)
        || word.Equals(Savings, StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns the model's account value into a simulator account name, or null if it isn't one.</summary>
    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "chequing" or "checking" => Chequing,
        "savings" or "saving" => Savings,
        _ => null
    };

    /// <summary>
    /// True when the customer's own words name the account.
    /// A typo or nickname fails this check and leads to a clarification question,
    /// which is the safe way to be wrong.
    /// </summary>
    public static bool IsNamedIn(string request, string account) =>
        account == Chequing
            ? ChequingWords().IsMatch(request)
            : SavingsWords().IsMatch(request);

    /// <summary>The account after "from" or "out of" in the customer's words, or null if there isn't one.</summary>
    public static string? StatedSource(string request) => AccountIn(SourcePattern().Match(request));

    /// <summary>The account after "to" or "into" in the customer's words, or null if there isn't one.</summary>
    public static string? StatedDestination(string request) => AccountIn(DestinationPattern().Match(request));

    private static string? AccountIn(Match match) => !match.Success
        ? null
        : match.Groups["account"].Value.StartsWith('s') || match.Groups["account"].Value.StartsWith('S') ? Savings : Chequing;

    [GeneratedRegex(@"\b(?:from|out\s+of)\s+(?:my\s+|your\s+|the\s+)?(?<account>chequing|chequings|checking|checkings|cheq|chq|savings?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SourcePattern();

    [GeneratedRegex(@"\b(?:to|into)\s+(?:my\s+|your\s+|the\s+)?(?<account>chequing|chequings|checking|checkings|cheq|chq|savings?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DestinationPattern();

    [GeneratedRegex(@"\b(chequing|chequings|checking|checkings|cheq|chq)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChequingWords();

    [GeneratedRegex(@"\b(savings?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SavingsWords();
}
