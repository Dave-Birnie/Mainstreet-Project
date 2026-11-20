using System.Globalization;
using System.Text.RegularExpressions;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// Finds the amounts a customer actually wrote: digits ("25", "$1,000", "1k", "3 thousand"),
/// words ("twenty-five", "a hundred", "four point five"), and dollars with cents ("twelve dollars and fifty cents").
/// Used to make sure an amount came from the customer and wasn't made up by the model.
/// </summary>
internal static partial class StatedAmounts
{
    #region Entry points

    /// <summary>True when the customer wrote this amount. "$4" and "4.00" count as the same.</summary>
    public static bool IsStatedIn(string request, decimal amount) => Read(request).Amounts.Contains(amount);

    /// <summary>The different amounts the customer wrote, such as 5 and 10 for "$5 or $10".</summary>
    public static IReadOnlyList<decimal> AmountsIn(string request) => Read(request).Amounts;

    /// <summary>
    /// True when the customer wrote a number too large to work with, such as a 72-digit amount.
    /// Models often miscopy long numbers, so this looks at what the customer typed, not at the model's copy.
    /// </summary>
    public static bool HasNumberTooLargeToRead(string request) => Read(request).TooLarge;

    #endregion

    #region Reading the text

    private sealed record Reading(IReadOnlyList<decimal> Amounts, bool TooLarge);

    // A number token ("-5", "1,000", "4.50") or a word made of letters.
    private sealed record Token(string Text, bool IsNumber);

    /// <summary>Splits the text into number and word tokens, then reads each written amount once.</summary>
    private static Reading Read(string request)
    {
        List<Token> tokens = TokenPattern().Matches(request.ToLowerInvariant())
            .Select(match => new Token(match.Value, match.Groups["number"].Success))
            .ToList();

        List<decimal> amounts = new List<decimal>();
        bool tooLarge = false;
        int position = 0;
        while (position < tokens.Count)
        {
            NumberRead read = ReadNumber(tokens, ref position);
            if (!read.Found)
            {
                position++;
                continue;
            }

            if (read.TooLarge)
            {
                tooLarge = true;
                continue;
            }

            decimal amount = read.Value;
            AddCents(tokens, ref position, ref amount);
            if (!amounts.Contains(amount))
            {
                amounts.Add(amount);
            }
        }

        return new Reading(amounts, tooLarge);
    }

    // Numbers such as "-5", "1,000" or "4.50", and words such as "twenty".
    [GeneratedRegex(@"(?<number>-?\d[\d,]*(?:\.\d+)?)|(?<word>[a-z]+)")]
    private static partial Regex TokenPattern();

    #endregion

    #region Reading one number

    /// <param name="Found">True when a number was read.</param>
    /// <param name="Value">The number.</param>
    /// <param name="TooLarge">True when the number was too large to work with.</param>
    private readonly record struct NumberRead(bool Found, decimal Value, bool TooLarge)
    {
        public static readonly NumberRead None = new NumberRead(false, 0m, false);

        public static readonly NumberRead Overflow = new NumberRead(true, 0m, true);

        public static NumberRead Of(decimal value) => new NumberRead(true, value, false);
    }

    private static readonly Dictionary<string, int> SmallNumbers = new Dictionary<string, int>
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
        ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17,
        ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40,
        ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90
    };

    // Words after a number that multiply it: "1k", "3 thousand", "5 grand", "two million".
    private static readonly Dictionary<string, decimal> Multipliers = new Dictionary<string, decimal>
    {
        ["k"] = 1_000m, ["thousand"] = 1_000m, ["grand"] = 1_000m, ["million"] = 1_000_000m, ["billion"] = 1_000_000_000m
    };

    /// <summary>Reads a number written in digits or words starting at this position, and moves past it.</summary>
    private static NumberRead ReadNumber(List<Token> tokens, ref int position)
    {
        Token token = tokens[position];
        if (!token.IsNumber)
        {
            return ReadNumberInWords(tokens, ref position);
        }

        position++;
        if (!decimal.TryParse(token.Text.Replace(",", string.Empty), // "1,000" means 1000.
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal value))
        {
            return NumberRead.Overflow;
        }

        if (position < tokens.Count && Multipliers.TryGetValue(tokens[position].Text, out decimal multiplier))
        {
            position++;
            return TryMultiply(value, multiplier, out decimal scaled) ? NumberRead.Of(scaled) : NumberRead.Overflow;
        }

        return NumberRead.Of(value);
    }

    /// <summary>
    /// Reads number words such as "one hundred and five", "two thousand five hundred", "a hundred",
    /// "four point five", or "a dollar". Every step is checked, so nonsense such as
    /// "hundred hundred hundred…" can't overflow.
    /// </summary>
    private static NumberRead ReadNumberInWords(List<Token> tokens, ref int position)
    {
        int start = position;
        string Word(int index) => index < tokens.Count && !tokens[index].IsNumber ? tokens[index].Text : string.Empty;

        // "a dollar", "a buck"
        if (Word(position) is "a" or "an" && Word(position + 1) is "dollar" or "buck")
        {
            position++;
            return NumberRead.Of(1m);
        }

        decimal total = 0m;   // The part already multiplied by thousand, million, and so on.
        decimal current = 0m; // The part still being built, up to 999.
        bool inNumber = false;

        // "a hundred", "a thousand"
        if (Word(position) is "a" or "an" && (Word(position + 1) == "hundred" || Multipliers.ContainsKey(Word(position + 1))))
        {
            current = 1m;
            inNumber = true;
            position++;
        }

        while (Word(position).Length > 0)
        {
            string word = Word(position);
            if (SmallNumbers.TryGetValue(word, out int small))
            {
                current += small;
                inNumber = true;
            }
            else if (word == "hundred" && inNumber)
            {
                if (!TryMultiply(current == 0m ? 1m : current, 100m, out current))
                {
                    position++;
                    return NumberRead.Overflow;
                }
            }
            else if (word != "k" && Multipliers.TryGetValue(word, out decimal multiplier) && inNumber)
            {
                if (!TryMultiply(current == 0m ? 1m : current, multiplier, out decimal part) || !TryAdd(total, part, out total))
                {
                    position++;
                    return NumberRead.Overflow;
                }

                current = 0m;
            }
            else if (word == "and" && inNumber && SmallNumbers.ContainsKey(Word(position + 1)))
            {
                // "one hundred and five": keep building the same number.
            }
            else
            {
                break;
            }

            position++;
        }

        if (!inNumber)
        {
            position = start;
            return NumberRead.None;
        }

        if (!TryAdd(total, current, out decimal whole))
        {
            return NumberRead.Overflow;
        }

        // "four point five" is 4.5, and "four point seven five" is 4.75.
        if (Word(position) == "point" && SmallNumbers.TryGetValue(Word(position + 1), out int firstDigit) && firstDigit < 10)
        {
            position++;
            string digits = string.Empty;
            while (SmallNumbers.TryGetValue(Word(position), out int digit) && digit < 10)
            {
                digits += digit.ToString(CultureInfo.InvariantCulture);
                position++;
            }

            whole += decimal.Parse("0." + digits, CultureInfo.InvariantCulture);
        }

        return NumberRead.Of(whole);
    }

    /// <summary>
    /// Adds cents when the amount is written as dollars and cents, such as "twelve dollars and fifty cents"
    /// or "12 dollars 50 cents". Leaves the position alone if the words don't follow that pattern.
    /// </summary>
    private static void AddCents(List<Token> tokens, ref int position, ref decimal amount)
    {
        int next = position;
        if (next >= tokens.Count || tokens[next].Text is not ("dollar" or "dollars" or "buck" or "bucks"))
        {
            return;
        }

        next++;
        if (next < tokens.Count && tokens[next].Text == "and")
        {
            next++;
        }

        if (next >= tokens.Count)
        {
            return;
        }

        NumberRead cents = ReadNumber(tokens, ref next);
        bool isWholeCents = cents.Found && !cents.TooLarge
                            && cents.Value is >= 0m and < 100m && cents.Value == decimal.Truncate(cents.Value);
        bool endsWithCents = next < tokens.Count && tokens[next].Text is "cent" or "cents";
        if (isWholeCents && endsWithCents)
        {
            amount += cents.Value / 100m;
            position = next + 1;
        }
    }

    #endregion

    #region Safe arithmetic

    private static bool TryMultiply(decimal left, decimal right, out decimal result)
    {
        try
        {
            result = left * right;
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    private static bool TryAdd(decimal left, decimal right, out decimal result)
    {
        try
        {
            result = left + right;
            return true;
        }
        catch (OverflowException)
        {
            result = 0m;
            return false;
        }
    }

    #endregion
}
