namespace BankingCli.NaturalLanguage;

/// <summary>
/// An AI provider that the startup key prompt can set up: where its OpenAI-compatible API lives,
/// and which model to use by default.
/// </summary>
/// <param name="Name">The name shown to the user.</param>
/// <param name="Endpoint">The base URL of the provider's OpenAI-compatible API.</param>
/// <param name="DefaultModel">A model that supports JSON mode, tested with this app.</param>
internal sealed record AiProvider(string Name, string Endpoint, string DefaultModel)
{
    public static readonly AiProvider OpenAi =
        new AiProvider("OpenAI", "https://api.openai.com/v1", "gpt-4o-mini");

    public static readonly AiProvider Gemini =
        new AiProvider("Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-3.8-flash");
}
