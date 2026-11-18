using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// A small client for the <c>/chat/completions</c> endpoint used by OpenAI and OpenAI-compatible providers.
/// Each call makes exactly one HTTP request and never retries.
/// </summary>
internal sealed class OpenAiCompatibleChatClient(HttpClient httpClient, AiSettings settings) : IChatClient
{
    // Long error text from the provider is cut to this many characters.
    private const int MaxProviderMessageLength = 200;

    #region Sending a request

    public async Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages, bool requireJson, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = BuildRequest(messages, requireJson);

        // Give up after the configured timeout, or sooner if the caller cancels.
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, timeout.Token);
            string responseText = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw new AiServiceException(DescribeHttpError(response.StatusCode, responseText));
            }

            return ReadReplyText(responseText);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiServiceException(
                $"the AI service did not respond within {settings.Timeout.TotalSeconds:0.##} seconds.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiServiceException(
                $"the AI service at {settings.ChatCompletionsUri.Host} could not be reached ({ex.Message}).", ex);
        }
    }

    /// <summary>Builds the POST request: model, messages, optional JSON mode, and the API key.</summary>
    private HttpRequestMessage BuildRequest(IReadOnlyList<ChatMessage> messages, bool requireJson)
    {
        JsonArray chat = new JsonArray();
        foreach (ChatMessage message in messages)
        {
            chat.Add(new JsonObject { ["role"] = message.Role, ["content"] = message.Content });
        }

        JsonObject body = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = chat
        };

        if (requireJson)
        {
            body["response_format"] = new JsonObject { ["type"] = "json_object" };
        }

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, settings.ChatCompletionsUri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        return request;
    }

    #endregion

    #region Reading the reply

    /// <summary>Pulls the reply text out of a successful response.</summary>
    /// <exception cref="AiServiceException">The response isn't the expected JSON, or the reply is empty.</exception>
    private static string ReadReplyText(string responseText)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(responseText);
        }
        catch (JsonException ex)
        {
            throw new AiServiceException("the AI service returned a response that was not valid JSON.", ex);
        }

        // Expected shape: { "choices": [ { "message": { "content": "..." } } ] }
        if (root is JsonObject response
            && response["choices"] is JsonArray { Count: > 0 } choices
            && choices[0] is JsonObject firstChoice
            && firstChoice["message"] is JsonObject message
            && message["content"] is JsonValue content
            && content.TryGetValue(out string? text)
            && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        throw new AiServiceException("the AI service returned an empty reply.");
    }

    #endregion

    #region Error messages

    /// <summary>Turns an HTTP error into a short explanation that says what to check.</summary>
    private string DescribeHttpError(HttpStatusCode status, string responseText)
    {
        int code = (int)status;
        return status switch
        {
            // Never repeat the provider's text here: some providers include part of the key in it.
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"the AI service rejected the API key (HTTP {code}). Check {AiSettings.ApiKeyVariable}.",

            HttpStatusCode.NotFound =>
                $"the AI service returned HTTP 404. Check {AiSettings.EndpointVariable} and that model " +
                $"\"{settings.Model}\" exists ({ProviderMessage(responseText)}).",

            HttpStatusCode.TooManyRequests =>
                "the AI service is rate limiting requests or the account is out of quota (HTTP 429).",

            _ => $"the AI service returned HTTP {code} ({ProviderMessage(responseText)})."
        };
    }

    /// <summary>The provider's own error message, on one line and shortened, or "no details".</summary>
    private static string ProviderMessage(string responseText)
    {
        string? message;
        try
        {
            // Usually { "error": { "message": "..." } }, but some providers send { "error": "..." }.
            message = JsonNode.Parse(responseText) is JsonObject response
                ? response["error"] switch
                {
                    JsonObject error => error["message"]?.ToString(),
                    JsonNode error => error.ToString(),
                    null => null
                }
                : null;
        }
        catch (JsonException)
        {
            message = responseText.Trim(); // Not JSON, e.g. an HTML error page.
        }

        message = message?.ReplaceLineEndings(" ");
        if (string.IsNullOrEmpty(message))
        {
            return "no details";
        }

        return message.Length <= MaxProviderMessageLength
            ? message
            : message[..MaxProviderMessageLength] + "...";
    }

    #endregion
}
