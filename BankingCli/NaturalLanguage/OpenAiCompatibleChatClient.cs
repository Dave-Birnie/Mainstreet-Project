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
                throw DescribeHttpError(response.StatusCode, responseText);
            }

            return ReadReplyText(responseText);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            string seconds = $"{settings.Timeout.TotalSeconds:0.##}";
            throw new AiServiceException(
                $"the AI service took longer than {seconds} seconds to answer",
                "Try again in a moment. If it keeps happening, check your internet connection, " +
                $"or allow more time by setting {AiSettings.TimeoutVariable}.",
                details: $"No response from {settings.ChatCompletionsUri.Host} within {seconds} seconds.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiServiceException(
                $"the AI service at {settings.ChatCompletionsUri.Host} couldn't be reached",
                $"Check your internet connection, and that {AiSettings.EndpointVariable} is the right address.",
                details: ex.Message,
                ex);
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
            throw UnreadableResponse("The response was not valid JSON.", ex);
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

        throw UnreadableResponse("The response had no reply text.");
    }

    private static AiServiceException UnreadableResponse(string details, Exception? innerException = null) =>
        new AiServiceException(
            "the AI service sent back a response I couldn't read",
            $"Try again. If it keeps happening, check that {AiSettings.EndpointVariable} points to an OpenAI-compatible API.",
            details,
            innerException);

    #endregion

    #region Error messages

    /// <summary>Turns an HTTP error into what went wrong and what to do about it.</summary>
    private AiServiceException DescribeHttpError(HttpStatusCode status, string responseText)
    {
        int code = (int)status;
        string details = $"HTTP {code}: {ProviderMessage(responseText)}";

        return status switch
        {
            // Never repeat the provider's text here: some providers include part of the key in it.
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new AiServiceException(
                "the AI service didn't accept the API key",
                "Check that the key is correct and still active, then restart the app with the right key.",
                details: $"HTTP {code}"),

            HttpStatusCode.NotFound => new AiServiceException(
                $"the AI service couldn't find the model \"{settings.Model}\"",
                $"Check that {AiSettings.ModelVariable} is a model your provider offers, " +
                $"and that {AiSettings.EndpointVariable} is correct.",
                details),

            HttpStatusCode.TooManyRequests => new AiServiceException(
                "the AI service is busy, or the account has run out of credit",
                "Wait a moment and try again. If it keeps happening, check your plan or billing with your AI provider.",
                details),

            >= HttpStatusCode.InternalServerError => new AiServiceException(
                "the AI service had a problem on its end",
                "Wait a moment and try again.",
                details),

            _ => new AiServiceException(
                "the AI service couldn't handle the request",
                $"Check that {AiSettings.ModelVariable} is a chat model that supports JSON output.",
                details)
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
