using System.Globalization;
using System.Text.RegularExpressions;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// Checks the model's conversational reply against what the simulator actually reported.
/// If a check fails, the reply is thrown away and the simulator's own result is shown instead.
/// </summary>
/// <remarks>
/// The checks are deliberately strict: rejecting a good reply only costs some friendliness,
/// while showing a wrong one would mislead the customer.
/// </remarks>
internal static partial class ReplyCheck
{
    #region Entry point

    /// <summary>Returns null when the reply agrees with the simulator, or a short description of what it got wrong.</summary>
    /// <param name="reply">The model's reply.</param>
    /// <param name="command">The simulator command that was run, such as "transfer savings chequing 4.00".</param>
    /// <param name="simulatorResult">The simulator's actual output.</param>
    public static string? FindProblem(string reply, string command, string simulatorResult)
    {
        Facts facts = Facts.From(command, simulatorResult);

        if (!MentionsOnlyKnownAmounts(reply, facts))
        {
            return "it mentioned an amount the simulator didn't report";
        }

        if (!PutsBalancesOnTheRightAccounts(reply, facts))
        {
            return "it gave a balance for the wrong account";
        }

        if (!DescribesTheRightOutcome(reply, facts))
        {
            return facts.WasDeclined
                ? "it didn't say the request was declined"
                : "it said a successful request didn't go through";
        }

        return null;
    }

    #endregion

    #region What the simulator reported

    /// <param name="RequestedAmount">The amount in the command, or null for a balance check.</param>
    /// <param name="CommandAccounts">The accounts the command used, such as savings and chequing for a transfer.</param>
    /// <param name="Balances">Each balance in the result, such as savings = 6.00.</param>
    /// <param name="WasDeclined">True when the result starts with ERROR.</param>
    /// <param name="IsBalanceCheck">True for a balance command, which can't succeed or fail.</param>
    private sealed record Facts(
        decimal? RequestedAmount,
        string[] CommandAccounts,
        Dictionary<string, decimal> Balances,
        bool WasDeclined,
        bool IsBalanceCheck)
    {
        public static Facts From(string command, string simulatorResult)
        {
            string[] commandWords = command.Split(' ');
            bool isBalanceCheck = commandWords[0] == "balance";

            decimal? requested = !isBalanceCheck && TryParseMoney(commandWords[^1], out decimal amount) ? amount : null;

            // "deposit savings 10.00" uses savings. "transfer savings chequing 4.00" uses both.
            string[] commandAccounts = commandWords.Where(Accounts.IsSimulatorName).ToArray();

            Dictionary<string, decimal> balances = new Dictionary<string, decimal>();
            foreach (Match match in BalancePattern().Matches(simulatorResult))
            {
                if (TryParseMoney(match.Groups["amount"].Value, out decimal balance))
                {
                    balances[match.Groups["account"].Value] = balance;
                }
            }

            bool wasDeclined = SimulatorErrors.Code(simulatorResult) is not null;
            return new Facts(requested, commandAccounts, balances, wasDeclined, isBalanceCheck);
        }

        /// <summary>True when the amount is the requested amount or one of the reported balances.</summary>
        public bool IsKnown(decimal amount) => amount == RequestedAmount || Balances.ContainsValue(amount);
    }

    // "savings=$6.00" in the simulator's result.
    [GeneratedRegex(@"(?<account>chequing|savings)=\$(?<amount>\d+(?:\.\d+)?)")]
    private static partial Regex BalancePattern();

    #endregion

    #region Check 1: every amount is one the simulator reported

    private static bool MentionsOnlyKnownAmounts(string reply, Facts facts) =>
        DollarAmounts(reply).All(facts.IsKnown);

    #endregion

    #region Check 2: each balance is on the right account

    /// <summary>
    /// Splits the reply into short clauses, such as "your savings balance is now $6.00". When a clause names exactly
    /// one account, each amount in it must be that account's balance, or the requested amount on an account the
    /// command used. This catches "$6.00 in chequing" when $6.00 was the savings balance.
    /// </summary>
    private static bool PutsBalancesOnTheRightAccounts(string reply, Facts facts)
    {
        foreach (string clause in ClauseBreaks().Split(reply))
        {
            List<string> namedAccounts = new List<string>();
            if (Accounts.IsNamedIn(clause, Accounts.Chequing))
            {
                namedAccounts.Add(Accounts.Chequing);
            }

            if (Accounts.IsNamedIn(clause, Accounts.Savings))
            {
                namedAccounts.Add(Accounts.Savings);
            }

            // A clause naming no account ("you now have $6.00") or both can't be tied to one account.
            if (namedAccounts.Count != 1)
            {
                continue;
            }

            string account = namedAccounts[0];
            foreach (decimal amount in DollarAmounts(clause))
            {
                bool isRequestedAmount = amount == facts.RequestedAmount && facts.CommandAccounts.Contains(account);
                bool isThisAccountsBalance = facts.Balances.TryGetValue(account, out decimal balance) && balance == amount;
                if (!isRequestedAmount && !isThisAccountsBalance)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // Sentence ends, commas and semicolons followed by a space, and joining words. "$1,046.00" isn't split.
    [GeneratedRegex(@"[.;!?](?=\s|$)|,(?=\s)|\b(?:and|but|while|whereas|so)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClauseBreaks();

    #endregion

    #region Check 3: success and decline are described correctly

    /// <summary>
    /// A declined request must be described as not happening, and a successful one must not be.
    /// Balance checks have no outcome to describe, so they're skipped.
    /// </summary>
    private static bool DescribesTheRightOutcome(string reply, Facts facts)
    {
        if (facts.IsBalanceCheck)
        {
            return true;
        }

        bool soundsDeclined = DeclineWords().IsMatch(reply);

        // "could not be completed" and "was not successful" describe a decline, not a success,
        // so negated success words are removed before looking for a success claim.
        bool soundsSuccessful = SuccessWords().IsMatch(NegatedSuccess().Replace(reply, string.Empty));

        return facts.WasDeclined
            ? soundsDeclined && !soundsSuccessful
            : !soundsDeclined;
    }

    // "not", "didn't", "couldn't", "insufficient", and so on.
    [GeneratedRegex(@"\b(?:not|cannot|unable|declined|insufficient|failed|unsuccessful)\b|n['’]t\b", RegexOptions.IgnoreCase)]
    private static partial Regex DeclineWords();

    [GeneratedRegex(@"\b(?:success|successful|successfully|completed|went through|all set)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SuccessWords();

    // "not", "never" or "n't", up to two more words, then a success word: "could not be completed".
    [GeneratedRegex(@"(?:\bnot\b|\bnever\b|n['’]t\b)(?:\s+\w+){0,2}?\s+(?:success|successful|successfully|completed|went through|all set)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NegatedSuccess();

    #endregion

    #region Reading amounts

    /// <summary>Every "$12.34"-style amount in the text. Thousands commas are ignored.</summary>
    private static IEnumerable<decimal> DollarAmounts(string text) =>
        DollarAmountPattern().Matches(text)
            .Select(match => TryParseMoney(match.Groups[1].Value, out decimal value) ? value : -1m); // -1 never matches.

    private static bool TryParseMoney(string text, out decimal value) =>
        decimal.TryParse(text.Replace(",", string.Empty), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);

    [GeneratedRegex(@"\$\s?(\d[\d,]*(?:\.\d+)?)")]
    private static partial Regex DollarAmountPattern();

    #endregion
}
