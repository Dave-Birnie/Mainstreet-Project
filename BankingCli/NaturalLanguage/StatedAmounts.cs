using System.Globalization;
using System.Text.RegularExpressions;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// Finds the numbers a customer actually wrote, in digits ("25", "$1,000") or words ("twenty-five", "a hundred").
/// Used to make sure an amount came from the customer and wasn't made up by the model.
/// </summary>
internal static partial class StatedAmounts
{
    #region Entry point

    /// <summary>True when the customer's text contains this amount. "$4" and "4.00" count as the same.</summary>
    public static bool IsStatedIn(string request, decimal amount) => NumbersIn(request).Contains(amount);

    /// <summary>Every number in the text, from digits and from number words.</summary>
    internal static List<decimal> NumbersIn(string request)
    {
        List<decimal> numbers = new List<decimal>();
        numbers.AddRange(NumbersInDigits(request));
        numbers.AddRange(NumbersInWords(request));
        return numbers;
    }

    #endregion

    #region Numbers written in digits

    private static IEnumerable<decimal> NumbersInDigits(string request)
    {
        foreach (Match match in DigitsPattern().Matches(request))
        {
            string digits = match.Value.Replace(",", string.Empty); // "1,000" means 1000.
            if (decimal.TryParse(digits, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out decimal value))
            {
                yield return value;
            }
        }
    }

    // An optional minus sign, digits with optional thousands commas, and optional decimals.
    [GeneratedRegex(@"-?\d[\d,]*(?:\.\d+)?")]
    private static partial Regex DigitsPattern();

    #endregion

    #region Numbers written in words

    private static readonly Dictionary<string, int> SmallNumbers = new Dictionary<string, int>
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
        ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17,
        ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40,
        ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90
    };

    private static readonly Dictionary<string, int> Scales = new Dictionary<string, int>
    {
        ["thousand"] = 1_000, ["million"] = 1_000_000, ["billion"] = 1_000_000_000
    };

    /// <summary>
    /// Reads runs of number words as numbers. For example, "one hundred and five" is 105,
    /// "two thousand five hundred" is 2500, and "a dollar" is 1.
    /// </summary>
    private static List<decimal> NumbersInWords(string request)
    {
        string[] words = WordPattern().Matches(request.ToLowerInvariant()).Select(match => match.Value).ToArray();
        List<decimal> found = new List<decimal>();

        decimal total = 0m;   // The part already multiplied by thousand, million, and so on.
        decimal current = 0m; // The part still being built, up to 999.
        bool inNumber = false;

        void FinishNumber()
        {
            if (inNumber)
            {
                found.Add(total + current);
            }

            total = 0m;
            current = 0m;
            inNumber = false;
        }

        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            string next = i + 1 < words.Length ? words[i + 1] : string.Empty;

            if (SmallNumbers.TryGetValue(word, out int small))
            {
                current += small;
                inNumber = true;
            }
            else if (word == "hundred" && inNumber)
            {
                current = (current == 0m ? 1m : current) * 100m;
            }
            else if (Scales.TryGetValue(word, out int scale) && inNumber)
            {
                total += (current == 0m ? 1m : current) * scale;
                current = 0m;
            }
            else if ((word is "a" or "an") && (next == "hundred" || Scales.ContainsKey(next)))
            {
                // "a hundred", "a thousand"
                FinishNumber();
                current = 1m;
                inNumber = true;
            }
            else if ((word is "a" or "an") && (next is "dollar" or "buck"))
            {
                // "a dollar", "a buck"
                FinishNumber();
                found.Add(1m);
            }
            else if (word == "and" && inNumber && SmallNumbers.ContainsKey(next))
            {
                // "one hundred and five": keep building the same number.
            }
            else
            {
                FinishNumber();
            }
        }

        FinishNumber();
        return found;
    }

    // Letters only, so "twenty-five" becomes "twenty" and "five".
    [GeneratedRegex(@"[a-z]+")]
    private static partial Regex WordPattern();

    #endregion
}
