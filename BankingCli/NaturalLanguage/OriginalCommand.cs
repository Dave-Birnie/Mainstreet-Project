namespace BankingCli.NaturalLanguage;

/// <summary>
/// Spots input that is shaped like one of the simulator's original commands.
/// That input goes straight to <see cref="BankSession"/> with no API call, so it behaves exactly
/// as it always did, including the simulator's own error messages.
/// </summary>
internal static class OriginalCommand
{
    /// <summary>
    /// True when the input starts with a command word, has that command's exact number of words,
    /// and uses real account names.
    /// </summary>
    /// <remarks>
    /// The amount is deliberately not checked, so "deposit savings -5" still gets the simulator's
    /// own ERROR INVALID_AMOUNT. Account names are checked so English such as "deposit 50 savings"
    /// is interpreted instead of failing.
    /// </remarks>
    public static bool IsRecognized(string input)
    {
        string[] words = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // A blank line goes to the simulator too, which prints nothing, as before.
        if (words.Length == 0)
        {
            return true;
        }

        return words[0].ToLowerInvariant() switch
        {
            "help" or "?" or "exit" => words.Length == 1,
            "balance" => words.Length == 1 || (words.Length == 2 && Accounts.IsSimulatorName(words[1])),
            "deposit" or "withdraw" => words.Length == 3 && Accounts.IsSimulatorName(words[1]),
            "transfer" => words.Length == 4 && Accounts.IsSimulatorName(words[1]) && Accounts.IsSimulatorName(words[2]),
            _ => false
        };
    }
}
