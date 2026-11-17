using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// Minimal client for the <c>/chat/completions</c> endpoint shared by OpenAI and OpenAI-compatible providers.
/// It makes exactly one request per call and never retries.
/// </summary>
internal sealed class OpenAiCompatibleChatClient(HttpClient httpClient, AiSettings settings) : IChatClient
{
    private const int MaxProviderMessageLength = 200;

    public async Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages, bool requireJson, CancellationToken cancellationToken)
    {
        JsonObject body = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = new JsonArray(messages
                .Select(message => (JsonNode)new JsonObject { ["role"] = message.Role, ["content"] = message.Content })
                .ToArray())
        };

        if (requireJson)
        {
            body["response_format"] = new JsonObject { ["type"] = "json_object" };
        }

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, settings.ChatCompletionsUri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

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

            return ReadMessageContent(responseText);
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

    private static string ReadMessageContent(string responseText)
    {
        string? text;
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseText);
            text = document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("choices", out JsonElement choices)
                   && choices.ValueKind == JsonValueKind.Array
                   && choices.GetArrayLength() > 0
                   && choices[0].ValueKind == JsonValueKind.Object
                   && choices[0].TryGetProperty("message", out JsonElement message)
                   && message.ValueKind == JsonValueKind.Object
                   && message.TryGetProperty("content", out JsonElement content)
                   && content.ValueKind == JsonValueKind.String
                ? content.GetString()
                : null;
        }
        catch (JsonException ex)
        {
            throw new AiServiceException("the AI service returned a response that was not valid JSON.", ex);
        }

        return string.IsNullOrWhiteSpace(text)
            ? throw new AiServiceException("the AI service returned an empty reply.")
            : text;
    }

    private string DescribeHttpError(HttpStatusCode status, string responseText)
    {
        int code = (int)status;
        return status switch
        {
            // Never echo the provider's text here: some providers include part of the key in it.
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"the AI service rejected the API key (HTTP {code}). Check {AiSettings.ApiKeyVariable}.",
            HttpStatusCode.NotFound =>
                $"the AI service returned HTTP 404. Check {AiSettings.EndpointVariable} and that model " +
                $"\"{settings.Model}\" exists ({ProviderMessage(responseText)}).",
            HttpStatusCode.TooManyRequests =>
                $"the AI service is rate limiting requests or the account is out of quota (HTTP 429).",
            _ => $"the AI service returned HTTP {code} ({ProviderMessage(responseText)})."
        };
    }

    private static string ProviderMessage(string responseText)
    {
        string message;
        try
        {
            JsonNode? error = JsonNode.Parse(responseText)?["error"];
            message = error?.GetValueKind() == JsonValueKind.Object
                ? error["message"]?.ToString() ?? "no details"
                : error?.ToString() ?? "no details";
        }
        catch (JsonException)
        {
            message = responseText.Trim();
        }

        message = message.ReplaceLineEndings(" ");
        if (message.Length == 0)
        {
            return "no details";
        }

        return message.Length <= MaxProviderMessageLength ? message : message[..MaxProviderMessageLength] + "...";
    }
}
