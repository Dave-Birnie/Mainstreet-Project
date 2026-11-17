namespace BankingCli.NaturalLanguage;

/// <summary>
/// Decides whether input is shaped like one of the simulator's original commands. Such input goes
/// straight to <see cref="BankSession"/> with no API call, so it behaves exactly as it always did,
/// including the simulator's own error messages.
/// </summary>
internal static class OriginalCommand
{
    public static bool IsRecognized(string input)
    {
        string[] tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return true;
        }

        // The amount is deliberately not checked, so "deposit savings -5" still gets the simulator's
        // ERROR INVALID_AMOUNT. Account names are checked so that English such as "deposit 50 savings"
        // is interpreted instead of failing as ERROR INVALID_ACCOUNT.
        return tokens[0].ToLowerInvariant() switch
        {
            "help" or "?" or "exit" => tokens.Length == 1,
            "balance" => tokens.Length == 1 || (tokens.Length == 2 && IsAccount(tokens[1])),
            "deposit" or "withdraw" => tokens.Length == 3 && IsAccount(tokens[1]),
            "transfer" => tokens.Length == 4 && IsAccount(tokens[1]) && IsAccount(tokens[2]),
            _ => false
        };
    }

    private static bool IsAccount(string token) =>
        token.Equals("chequing", StringComparison.OrdinalIgnoreCase)
        || token.Equals("savings", StringComparison.OrdinalIgnoreCase);
}
