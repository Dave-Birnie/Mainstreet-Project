using System.Text;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// Offers to take an API key at startup when none is set. The key is hidden as it's typed,
/// used for this session only, and never saved.
/// </summary>
internal static class ApiKeyPrompt
{
    /// <summary>Asks whether the user wants to enter a key. Returns the key, or null if they'd rather not.</summary>
    public static string? Ask()
    {
        Console.WriteLine("No API key is set, so only the original commands are available right now.");
        Console.Write("Would you like to enter an API key to turn on everyday-English requests? (y/n): ");

        string? answer = Console.ReadLine();
        if (answer is null || answer.Trim().ToLowerInvariant() is not ("y" or "yes"))
        {
            return null;
        }

        Console.Write("API key (hidden as you type, not saved): ");
        string key = ReadHidden().Trim();
        return key.Length > 0 ? key : null;
    }

    /// <summary>Reads a line without showing it. Each character appears as *, and Backspace works.</summary>
    private static string ReadHidden()
    {
        StringBuilder typed = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return typed.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0)
                {
                    typed.Length--;
                    Console.Write("\b \b"); // Erase the last *.
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }
}
