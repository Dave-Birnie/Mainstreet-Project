using System.Globalization;
using System.Text.RegularExpressions;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// Checks the model's conversational reply against what the simulator actually reported.
/// If a check fails, the reply is thrown away and the simulator's own result is shown instead.
/// </summary>
/// <remarks>
/// These are deliberately strict, word-based checks. Rejecting a good reply only costs some friendliness,
/// while showing a wrong one would mislead the customer. They catch the common ways a reply can be wrong,
/// but free text can't be verified completely.
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

        if (AmountInWords().IsMatch(reply))
        {
            return "it wrote an amount in words instead of as a figure";
        }

        if (!UsesEachAmountCorrectly(reply, facts))
        {
            return "it gave a wrong amount or balance";
        }

        if (!NamesTheRightAccounts(reply, facts))
        {
            return "it named the wrong account";
        }

        if (!DescribesTheRightOperation(reply, facts))
        {
            return "it described a different kind of transaction";
        }

        if (!DescribesTheRightOutcome(reply, facts))
        {
            return facts.WasDeclined
                ? "it didn't say the request was declined"
                : "it said a successful request didn't go through";
        }

        if (!GivesTheRealReason(reply, facts))
        {
            return "it gave a different reason for the decline";
        }

        return null;
    }

    #endregion

    #region What the simulator reported

    /// <param name="Operation">"deposit", "withdraw", "transfer" or "balance".</param>
    /// <param name="ErrorCode">The simulator's error code, such as INSUFFICIENT_FUNDS, or null on success.</param>
    /// <param name="IsBalanceCheck">True for a balance command, which moves no money.</param>
    /// <param name="RequestedAmount">The amount in the command, or null for a balance check.</param>
    /// <param name="FromAccount">The account money comes out of: a withdrawal's account, or a transfer's source.</param>
    /// <param name="ToAccount">The account money goes into: a deposit's account, or a transfer's destination.</param>
    /// <param name="Balances">Each balance in the result, such as savings = 6.00.</param>
    /// <param name="WasDeclined">True when the result starts with ERROR.</param>
    private sealed record Facts(
        string Operation,
        string? ErrorCode,
        bool IsBalanceCheck,
        decimal? RequestedAmount,
        string? FromAccount,
        string? ToAccount,
        Dictionary<string, decimal> Balances,
        bool WasDeclined)
    {
        public static Facts From(string command, string simulatorResult)
        {
            // Commands look like "deposit savings 10.00", "withdraw chequing 5.00",
            // "transfer savings chequing 4.00" or "balance".
            string[] words = command.Split(' ');
            string operation = words[0];
            bool isBalanceCheck = operation == "balance";

            decimal? requested = !isBalanceCheck && TryParseMoney(words[^1], out decimal amount) ? amount : null;
            string? from = operation is "withdraw" or "transfer" ? words[1] : null;
            string? to = operation switch
            {
                "deposit" => words[1],
                "transfer" => words[2],
                _ => null
            };

            Dictionary<string, decimal> balances = new Dictionary<string, decimal>();
            foreach (Match match in BalancePattern().Matches(simulatorResult))
            {
                if (TryParseMoney(match.Groups["amount"].Value, out decimal balance))
                {
                    balances[match.Groups["account"].Value] = balance;
                }
            }

            string? errorCode = SimulatorErrors.Code(simulatorResult);
            return new Facts(operation, errorCode, isBalanceCheck, requested, from, to, balances, errorCode is not null);
        }

        /// <summary>True when the amount is the requested amount or one of the reported balances.</summary>
        public bool IsKnown(decimal amount) => amount == RequestedAmount || Balances.ContainsValue(amount);

        /// <summary>True when this account's reported balance is exactly this amount.</summary>
        public bool IsBalanceOf(string account, decimal amount) =>
            Balances.TryGetValue(account, out decimal balance) && balance == amount;

        /// <summary>True when the command took money out of, or put money into, this account.</summary>
        public bool UsedAccount(string account) => account == FromAccount || account == ToAccount;
    }

    // "savings=$6.00" in the simulator's result.
    [GeneratedRegex(@"(?<account>chequing|savings)=\$(?<amount>\d+(?:\.\d+)?)")]
    private static partial Regex BalancePattern();

    #endregion

    #region Check 1: every amount is one the simulator reported

    private static bool MentionsOnlyKnownAmounts(string reply, Facts facts) =>
        DollarAmountPattern().Matches(reply).All(match => TryReadAmount(match, out decimal amount) && facts.IsKnown(amount));

    #endregion

    #region Check 2: each amount is used the right way

    private enum AmountRole
    {
        Moved,   // "transferred $4.00", "a withdrawal of $25.00"
        Balance, // "your savings balance is $6.00", "you now have $4.00 in chequing"
        Unclear  // No nearby word says which.
    }

    /// <summary>
    /// Reads each dollar amount in its clause. An amount that was moved must be the requested amount.
    /// A balance must be the right account's balance. This catches "$4.00" called the savings balance
    /// when savings had $6.00, and "moved $6.00" when $4.00 was moved.
    /// </summary>
    private static bool UsesEachAmountCorrectly(string reply, Facts facts)
    {
        foreach (string sentence in SentenceBreaks().Split(reply))
        {
            // "Your savings and chequing balances are $6.00 and $4.00, respectively" pairs them in order.
            if (RespectivelyWord().IsMatch(sentence))
            {
                if (!PairsRespectively(sentence, facts))
                {
                    return false;
                }

                continue;
            }

            if (!ClausesUseAmountsCorrectly(sentence, facts))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Pairs the accounts and the balances in the order they're written.</summary>
    private static bool PairsRespectively(string sentence, Facts facts)
    {
        List<string> accounts = AccountWords().Matches(sentence).Select(match => AccountFor(match.Value)).ToList();
        List<decimal> amounts = DollarAmountPattern().Matches(sentence)
            .Select(match => TryReadAmount(match, out decimal amount) ? amount : -1m)
            .ToList();

        return accounts.Count == amounts.Count
               && accounts.Zip(amounts).All(pair => facts.IsBalanceOf(pair.First, pair.Second));
    }

    private static bool ClausesUseAmountsCorrectly(string sentence, Facts facts)
    {
        foreach (string clause in ClauseBreaks().Split(sentence))
        {
            List<Match> amounts = DollarAmountPattern().Matches(clause).ToList();
            for (int i = 0; i < amounts.Count; i++)
            {
                Match match = amounts[i];
                if (!TryReadAmount(match, out decimal amount))
                {
                    return false;
                }

                // Only look at the words between this amount and its neighbours.
                int start = i == 0 ? 0 : amounts[i - 1].Index + amounts[i - 1].Length;
                int end = i == amounts.Count - 1 ? clause.Length : amounts[i + 1].Index;
                string wordsBefore = clause[start..match.Index];
                string wordsAfter = clause[(match.Index + match.Length)..end];

                AmountRole role = RoleOf(wordsBefore, wordsAfter);
                string? account = NearestAccount(clause, match.Index);

                bool isCorrect = role switch
                {
                    AmountRole.Moved => amount == facts.RequestedAmount,
                    AmountRole.Balance => account is null
                        ? facts.Balances.ContainsValue(amount)
                        : facts.IsBalanceOf(account, amount),
                    _ => account is null
                        ? facts.IsKnown(amount)
                        : facts.IsBalanceOf(account, amount) || (amount == facts.RequestedAmount && facts.UsedAccount(account))
                };

                if (!isCorrect)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Decides from the nearest telling word: the last one before the amount, or else the first one after it.</summary>
    private static AmountRole RoleOf(string wordsBefore, string wordsAfter)
    {
        Match? moved = MovedWords().Matches(wordsBefore).LastOrDefault();
        Match? balance = BalanceWords().Matches(wordsBefore).LastOrDefault();
        if (moved is not null || balance is not null)
        {
            return balance is null || (moved is not null && moved.Index > balance.Index)
                ? AmountRole.Moved
                : AmountRole.Balance;
        }

        Match movedAfter = MovedWords().Match(wordsAfter);
        Match balanceAfter = BalanceWords().Match(wordsAfter);
        if (movedAfter.Success || balanceAfter.Success)
        {
            return !balanceAfter.Success || (movedAfter.Success && movedAfter.Index < balanceAfter.Index)
                ? AmountRole.Moved
                : AmountRole.Balance;
        }

        return AmountRole.Unclear;
    }

    /// <summary>The account named closest to the amount in this clause, or null if the clause names none.</summary>
    private static string? NearestAccount(string clause, int position) =>
        AccountWords().Matches(clause)
            .OrderBy(match => Math.Abs(match.Index - position))
            .Select(match => AccountFor(match.Value))
            .FirstOrDefault();

    private static string AccountFor(string word) =>
        word.StartsWith("s", StringComparison.OrdinalIgnoreCase) ? Accounts.Savings : Accounts.Chequing;

    [GeneratedRegex(@"[.;!?](?=\s|$)")]
    private static partial Regex SentenceBreaks();

    [GeneratedRegex(@"\brespectively\b", RegexOptions.IgnoreCase)]
    private static partial Regex RespectivelyWord();

    // "six dollars", "4 dollars", "fifty cents": amounts that should have been written as "$6.00".
    [GeneratedRegex(@"\b(?:\d[\d,]*(?:\.\d+)?|a|an|zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundred|thousand|million)\s+(?:dollars?|bucks?|cents?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AmountInWords();

    // Sentence ends, commas and semicolons followed by a space, and joining words. "$1,046.00" isn't split.
    [GeneratedRegex(@"[.;!?](?=\s|$)|,(?=\s)|\b(?:and|but|while|whereas|so)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClauseBreaks();

    // Words that say an amount was moved.
    [GeneratedRegex(@"\b(?:transfer\w*|mov(?:e|ed|es|ing)|deposit\w*|withdr\w*|took|take|taken|put|sent|send|added|add|out)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MovedWords();

    // Words that say an amount is a balance. "has been" doesn't count: "$4.00 has been transferred" is an amount moved.
    [GeneratedRegex(@"\b(?:balances?|ha(?:ve|s|d)(?!\s+been)|left|leaving|remains?|remaining|available|total|now|at|with|holds?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BalanceWords();

    [GeneratedRegex(@"\b(?:chequing|chequings|checking|checkings|cheq|chq|savings?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AccountWords();

    #endregion

    #region Check 3: money moves between the right accounts

    /// <summary>
    /// Every "from [account]" must be where the money came from, and every "to" or "into [account]" must be where
    /// it went. This catches "I moved $4.00 from chequing to savings" when it went from savings to chequing.
    /// </summary>
    private static bool NamesTheRightAccounts(string reply, Facts facts)
    {
        if (facts.IsBalanceCheck)
        {
            return true;
        }

        bool fromIsRight = FromAccountPattern().Matches(reply)
            .All(match => Accounts.Normalize(match.Groups["account"].Value) == facts.FromAccount);
        bool toIsRight = ToAccountPattern().Matches(reply)
            .All(match => Accounts.Normalize(match.Groups["account"].Value) == facts.ToAccount);

        return fromIsRight && toIsRight;
    }

    [GeneratedRegex(@"\bfrom\s+(?:your\s+|the\s+|my\s+)?(?<account>chequing|checking|savings?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FromAccountPattern();

    [GeneratedRegex(@"\b(?:to|into)\s+(?:your\s+|the\s+|my\s+)?(?<account>chequing|checking|savings?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ToAccountPattern();

    #endregion

    #region Check 4: the right kind of transaction is described

    /// <summary>A transfer mustn't be described as a withdrawal or deposit, and so on. "I withdrew $4.00" after a transfer fails.</summary>
    private static bool DescribesTheRightOperation(string reply, Facts facts) => facts.Operation switch
    {
        "deposit" => !WithdrawalWords().IsMatch(reply) && !TransferWords().IsMatch(reply),
        "withdraw" => !DepositWords().IsMatch(reply) && !TransferWords().IsMatch(reply),
        "transfer" => !WithdrawalWords().IsMatch(reply) && !DepositWords().IsMatch(reply),
        _ => true
    };

    [GeneratedRegex(@"\bwithdr\w*|\btook\s+out\b|\btaken\s+out\b", RegexOptions.IgnoreCase)]
    private static partial Regex WithdrawalWords();

    [GeneratedRegex(@"\bdeposit\w*", RegexOptions.IgnoreCase)]
    private static partial Regex DepositWords();

    [GeneratedRegex(@"\btransfer\w*", RegexOptions.IgnoreCase)]
    private static partial Regex TransferWords();

    #endregion

    #region Check 6: a decline gives the simulator's reason, not another one

    /// <summary>
    /// A declined request must give the simulator's actual reason ("insufficient funds", "the same account"),
    /// and mustn't invent another one ("your account is frozen").
    /// </summary>
    private static bool GivesTheRealReason(string reply, Facts facts)
    {
        if (!facts.WasDeclined)
        {
            return true;
        }

        if (InventedReasons().IsMatch(reply))
        {
            return false;
        }

        Regex? realReason = facts.ErrorCode switch
        {
            "INSUFFICIENT_FUNDS" => InsufficientFundsReason(),
            "SAME_ACCOUNT" => SameAccountReason(),
            "INVALID_AMOUNT" => InvalidAmountReason(),
            "BALANCE_LIMIT" => BalanceLimitReason(),
            _ => null
        };

        return realReason is null || realReason.IsMatch(reply);
    }

    [GeneratedRegex(@"\binsufficient\b|\benough\b|\bonly\s+(?:has|have|had)\b|\bonly\s+\$", RegexOptions.IgnoreCase)]
    private static partial Regex InsufficientFundsReason();

    [GeneratedRegex(@"\bsame\s+account\b|\bdifferent\s+accounts?\b|\btwo\s+(?:different\s+)?accounts\b|\bitself\b", RegexOptions.IgnoreCase)]
    private static partial Regex SameAccountReason();

    [GeneratedRegex(@"\bamount\b|\binvalid\b|\blimit\b", RegexOptions.IgnoreCase)]
    private static partial Regex InvalidAmountReason();

    [GeneratedRegex(@"\blimit\b|\bmaximum\b", RegexOptions.IgnoreCase)]
    private static partial Regex BalanceLimitReason();

    // Reasons the simulator never gives.
    [GeneratedRegex(@"\b(?:frozen|freeze|locked|suspended|fraud\w*|closed|blocked|overdrawn|overdraft)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InventedReasons();

    #endregion

    #region Check 5: success and decline are described correctly

    /// <summary>
    /// A declined request must be described with a clear decline ("was declined", "couldn't", "insufficient funds")
    /// and must not claim anything happened ("I withdrew", "successfully"). A successful one must not be described
    /// as failing. Balance checks have no outcome to describe, so they're skipped.
    /// </summary>
    private static bool DescribesTheRightOutcome(string reply, Facts facts)
    {
        if (facts.IsBalanceCheck)
        {
            return true;
        }

        bool soundsFailed = FailureWords().IsMatch(reply);
        if (!facts.WasDeclined)
        {
            return !soundsFailed;
        }

        // "could not be completed" and "no money was moved" describe a decline, so negated claims are removed first.
        bool claimsItHappened = HappenedWords().IsMatch(NegatedHappened().Replace(reply, string.Empty));
        bool saysNothingChanged = NothingChangedWords().IsMatch(reply);

        return (soundsFailed || saysNothingChanged) && !claimsItHappened;
    }

    // "was declined", "couldn't", "did not", "insufficient", "not enough", "doesn't have enough". A bare "not",
    // as in "you do not need to do anything", doesn't count.
    [GeneratedRegex(@"\b(?:declined|insufficient|unable|failed|unsuccessful|cannot)\b|\bnot\s+enough\b|n['’]t\s+have\s+enough\b|\bnot\s+have\s+enough\b|\b(?:could|did|was|were|has|have|had|is|can|would|will)(?:n['’]t|\s+not)\b|\bwon['’]t\b", RegexOptions.IgnoreCase)]
    private static partial Regex FailureWords();

    // "unchanged", "no balance changed", "no changes were made".
    [GeneratedRegex(@"\bunchanged\b|\bno\s+(?:balance|balances|money|funds|change|changes|transfer|withdrawal|deposit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NothingChangedWords();

    // Words that claim the money moved, or that the request succeeded.
    [GeneratedRegex(@"\b(?:withdrew|withdrawn|deposited|transferred|moved|took\s+out|taken\s+out|sent|added|went\s+through|success|successful|successfully|completed|all\s+set|done)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HappenedWords();

    // A negation, up to three more words, then a claim word: "could not be completed", "no money was moved".
    [GeneratedRegex(@"(?:\bnot\b|\bnever\b|\bno\b|\bnothing\b|n['’]t\b)(?:\s+\w+){0,3}?\s+(?:withdrew|withdrawn|deposited|transferred|moved|took\s+out|taken\s+out|sent|added|went\s+through|success|successful|successfully|completed|all\s+set|done)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NegatedHappened();

    #endregion

    #region Reading amounts

    private static bool TryReadAmount(Match dollarMatch, out decimal amount) =>
        TryParseMoney(dollarMatch.Groups[1].Value, out amount);

    private static bool TryParseMoney(string text, out decimal value) =>
        decimal.TryParse(text.Replace(",", string.Empty), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);

    // "$12.34" or "$1,046.00". Thousands commas are ignored when reading.
    [GeneratedRegex(@"\$\s?(\d[\d,]*(?:\.\d+)?)")]
    private static partial Regex DollarAmountPattern();

    #endregion
}
