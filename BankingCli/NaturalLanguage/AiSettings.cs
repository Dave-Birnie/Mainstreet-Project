using System.Globalization;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// Connection settings for the OpenAI-compatible API, read from environment variables so that
/// no credentials live in source control.
/// </summary>
internal sealed record AiSettings(Uri ChatCompletionsUri, string Model, string ApiKey, TimeSpan Timeout)
{
    public const string EndpointVariable = "BANKING_AI_ENDPOINT";
    public const string ModelVariable = "BANKING_AI_MODEL";
    public const string ApiKeyVariable = "BANKING_AI_API_KEY";
    public const string FallbackApiKeyVariable = "OPENAI_API_KEY";
    public const string TimeoutVariable = "BANKING_AI_TIMEOUT_SECONDS";

    public const string DefaultEndpoint = "https://api.openai.com/v1";
    public const string DefaultModel = "gpt-4o-mini";
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>
    /// Reads the settings. Returns false with a readable explanation when they are missing or invalid.
    /// </summary>
    public static bool TryLoad(Func<string, string?> getVariable, out AiSettings? settings, out string problem)
    {
        settings = null;
        problem = string.Empty;

        string? apiKey = Read(getVariable, ApiKeyVariable) ?? Read(getVariable, FallbackApiKeyVariable);
        if (apiKey is null)
        {
            problem = $"{ApiKeyVariable} is not set.";
            return false;
        }

        string endpoint = Read(getVariable, EndpointVariable) ?? DefaultEndpoint;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            problem = $"{EndpointVariable} must be an http or https URL, such as {DefaultEndpoint}.";
            return false;
        }

        int timeoutSeconds = DefaultTimeoutSeconds;
        string? timeoutText = Read(getVariable, TimeoutVariable);
        if (timeoutText is not null
            && (!int.TryParse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out timeoutSeconds)
                || timeoutSeconds <= 0))
        {
            problem = $"{TimeoutVariable} must be a whole number of seconds greater than zero.";
            return false;
        }

        settings = new AiSettings(
            ToChatCompletionsUri(baseUri),
            Read(getVariable, ModelVariable) ?? DefaultModel,
            apiKey,
            TimeSpan.FromSeconds(timeoutSeconds));
        return true;
    }

    // Accept either a base URL (https://host/v1) or the full chat completions URL.
    private static Uri ToChatCompletionsUri(Uri baseUri)
    {
        string url = baseUri.AbsoluteUri.TrimEnd('/');
        return url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? new Uri(url)
            : new Uri(url + "/chat/completions");
    }

    private static string? Read(Func<string, string?> getVariable, string name)
    {
        string? value = getVariable(name)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    // Keep the key out of logs and exception messages if the record is ever printed.
    public override string ToString() => $"AiSettings {{ Endpoint = {ChatCompletionsUri}, Model = {Model} }}";
}
