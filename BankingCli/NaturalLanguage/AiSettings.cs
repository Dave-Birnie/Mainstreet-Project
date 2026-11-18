using System.Globalization;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// How to reach the OpenAI-compatible API. Read from environment variables, so no credentials
/// are ever stored in the code or the repo.
/// </summary>
/// <param name="ChatCompletionsUri">The full URL of the chat completions endpoint.</param>
/// <param name="Model">The model ID, such as "gpt-4o-mini".</param>
/// <param name="ApiKey">The API key, sent as a Bearer token.</param>
/// <param name="Timeout">How long to wait for each API call.</param>
internal sealed record AiSettings(Uri ChatCompletionsUri, string Model, string ApiKey, TimeSpan Timeout)
{
    #region Environment variables and defaults

    public const string ApiKeyVariable = "BANKING_AI_API_KEY";
    public const string FallbackApiKeyVariable = "OPENAI_API_KEY";
    public const string EndpointVariable = "BANKING_AI_ENDPOINT";
    public const string ModelVariable = "BANKING_AI_MODEL";
    public const string TimeoutVariable = "BANKING_AI_TIMEOUT_SECONDS";

    public const string DefaultEndpoint = "https://api.openai.com/v1";
    public const string DefaultModel = "gpt-4o-mini";
    public const int DefaultTimeoutSeconds = 30;

    #endregion

    #region Loading

    /// <summary>
    /// Reads the settings. Returns false, with a readable explanation, when they are missing or invalid.
    /// </summary>
    /// <param name="getVariable">Looks up an environment variable. Tests pass in their own values.</param>
    public static bool TryLoad(Func<string, string?> getVariable, out AiSettings? settings, out string problem)
    {
        settings = null;
        problem = string.Empty;

        // API key: required.
        string? apiKey = Read(getVariable, ApiKeyVariable) ?? Read(getVariable, FallbackApiKeyVariable);
        if (apiKey is null)
        {
            problem = $"{ApiKeyVariable} is not set.";
            return false;
        }

        // Endpoint: optional, but must be an http or https URL.
        string endpoint = Read(getVariable, EndpointVariable) ?? DefaultEndpoint;
        bool isWebUrl = Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? baseUri)
                        && (baseUri.Scheme == Uri.UriSchemeHttps || baseUri.Scheme == Uri.UriSchemeHttp);
        if (!isWebUrl)
        {
            problem = $"{EndpointVariable} must be an http or https URL, such as {DefaultEndpoint}.";
            return false;
        }

        // Timeout: optional, but must be a whole number of seconds.
        int timeoutSeconds = DefaultTimeoutSeconds;
        string? timeoutText = Read(getVariable, TimeoutVariable);
        if (timeoutText is not null)
        {
            bool isWholeNumber = int.TryParse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture,
                out timeoutSeconds);
            if (!isWholeNumber || timeoutSeconds <= 0)
            {
                problem = $"{TimeoutVariable} must be a whole number of seconds greater than zero.";
                return false;
            }
        }

        settings = new AiSettings(
            ToChatCompletionsUri(baseUri!),
            Read(getVariable, ModelVariable) ?? DefaultModel,
            apiKey,
            TimeSpan.FromSeconds(timeoutSeconds));
        return true;
    }

    #endregion

    #region Helpers

    /// <summary>Accepts a base URL (https://host/v1) or the full chat completions URL.</summary>
    private static Uri ToChatCompletionsUri(Uri baseUri)
    {
        string url = baseUri.AbsoluteUri.TrimEnd('/');
        return url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? new Uri(url)
            : new Uri(url + "/chat/completions");
    }

    /// <summary>Reads a variable, treating blank values as not set.</summary>
    private static string? Read(Func<string, string?> getVariable, string name)
    {
        string? value = getVariable(name)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Leaves the API key out, in case the settings are ever printed or logged.</summary>
    public override string ToString() => $"AiSettings {{ Endpoint = {ChatCompletionsUri}, Model = {Model} }}";

    #endregion
}
