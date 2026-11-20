using System.Text;

namespace BankingCli.NaturalLanguage;

/// <summary>A key typed in at startup, and the provider it belongs to.</summary>
internal sealed record EnteredKey(AiProvider Provider, string Key);

/// <summary>
/// Offers to take an API key at startup when none is set. The user picks the provider, so the key
/// goes to the right service. The key is hidden as it's typed, used for this session only, and never saved.
/// </summary>
internal static class ApiKeyPrompt
{
    #region Asking

    /// <summary>Asks whether the user wants to enter a key, and for which provider. Returns null if they'd rather not.</summary>
    public static EnteredKey? Ask()
    {
        Console.WriteLine("No API key is set, so only the original commands are available right now.");
        Console.Write("Would you like to enter a Gemini or ChatGPT API key to turn on everyday-English requests? " +
                      "(For other OpenAI-compatible providers, see the README.) (y/n): ");

        string? answer = Console.ReadLine();
        if (answer is null || answer.Trim().ToLowerInvariant() is not ("y" or "yes"))
        {
            return null;
        }

        AiProvider? provider = AskForProvider();
        if (provider is null)
        {
            return null;
        }

        Console.Write($"{provider.Name} API key (hidden as you type, not saved): ");
        string key = ReadHidden().Trim();
        return key.Length > 0 ? new EnteredKey(provider, key) : null;
    }

    /// <summary>Asks until the answer is 1 (OpenAI) or 2 (Google Gemini). Returns null if input ends.</summary>
    private static AiProvider? AskForProvider()
    {
        while (true)
        {
            Console.Write("Which provider is the key for? Type 1 for ChatGPT (OpenAI) or 2 for Google Gemini: ");
            string? choice = Console.ReadLine()?.Trim().ToLowerInvariant();

            switch (choice)
            {
                case null:
                    return null;
                case "1" or "openai" or "chatgpt":
                    return AiProvider.OpenAi;
                case "2" or "gemini" or "google":
                    return AiProvider.Gemini;
            }
        }
    }

    #endregion

    #region Using the key

    /// <summary>
    /// Settings for this session: the typed key, plus the chosen provider's endpoint and default model.
    /// Every other setting still comes from the environment.
    /// </summary>
    public static Func<string, string?> SettingsWith(EnteredKey entered, Func<string, string?> getVariable) =>
        name => name switch
        {
            AiSettings.ApiKeyVariable => entered.Key,
            AiSettings.EndpointVariable => entered.Provider.Endpoint,
            AiSettings.ModelVariable => entered.Provider.DefaultModel,
            _ => getVariable(name)
        };

    #endregion

    #region Reading hidden input

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

    #endregion
}
