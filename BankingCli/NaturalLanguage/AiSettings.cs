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
    /// <summary>Where the key came from, for the status command, such as "from BANKING_AI_API_KEY".</summary>
    public string ApiKeySource { get; init; } = $"from {ApiKeyVariable}";

    #region Environment variables and defaults

    public const string ApiKeyVariable = "BANKING_AI_API_KEY";
    public const string FallbackApiKeyVariable = "OPENAI_API_KEY";
    public const string EndpointVariable = "BANKING_AI_ENDPOINT";
    public const string ModelVariable = "BANKING_AI_MODEL";
    public const string TimeoutVariable = "BANKING_AI_TIMEOUT_SECONDS";

    // Without other settings, the app uses OpenAI.
    public static readonly string DefaultEndpoint = AiProvider.OpenAi.Endpoint;
    public static readonly string DefaultModel = AiProvider.OpenAi.DefaultModel;
    public const int DefaultTimeoutSeconds = 30;

    #endregion

    #region Loading

    /// <summary>True when an API key is set in either supported variable.</summary>
    public static bool HasApiKey(Func<string, string?> getVariable) =>
        Read(getVariable, ApiKeyVariable) is not null || Read(getVariable, FallbackApiKeyVariable) is not null;

    /// <summary>
    /// Reads the settings. Returns false, with a readable explanation, when they are missing or invalid.
    /// </summary>
    /// <param name="getVariable">Looks up an environment variable. Tests pass in their own values.</param>
    public static bool TryLoad(Func<string, string?> getVariable, out AiSettings? settings, out string problem)
    {
        settings = null;
        problem = string.Empty;

        // API key: required.
        string? apiKey = Read(getVariable, ApiKeyVariable);
        string keySource = $"from {ApiKeyVariable}";
        if (apiKey is null)
        {
            apiKey = Read(getVariable, FallbackApiKeyVariable);
            keySource = $"from {FallbackApiKeyVariable}";
        }

        if (apiKey is null)
        {
            problem = "No API key is set.";
            return false;
        }

        // Endpoint: optional, but must be an http or https URL.
        string endpoint = Read(getVariable, EndpointVariable) ?? DefaultEndpoint;
        bool isWebUrl = Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? baseUri)
                        && (baseUri.Scheme == Uri.UriSchemeHttps || baseUri.Scheme == Uri.UriSchemeHttp);
        if (!isWebUrl)
        {
            problem = $"{EndpointVariable} isn't a valid web address. Set it to an http or https URL, such as {DefaultEndpoint}.";
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
                problem = $"{TimeoutVariable} isn't a whole number of seconds. Set it to a number such as 30.";
                return false;
            }
        }

        settings = new AiSettings(
            ToChatCompletionsUri(baseUri!),
            Read(getVariable, ModelVariable) ?? DefaultModel,
            apiKey,
            TimeSpan.FromSeconds(timeoutSeconds))
        {
            ApiKeySource = keySource
        };
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
