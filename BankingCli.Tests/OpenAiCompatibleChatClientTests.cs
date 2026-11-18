using System.Net;
using System.Text.Json.Nodes;
using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

/// <summary>Checks the HTTP requests sent to the API and the errors shown when it fails.</summary>
public class OpenAiCompatibleChatClientTests
{
    private static readonly AiSettings Settings = new AiSettings(
        new Uri("https://llm.example.test/v1/chat/completions"), "test-model", "test-key", TimeSpan.FromSeconds(5));

    private static readonly IReadOnlyList<ChatMessage> Messages =
        [ChatMessage.System("system prompt"), ChatMessage.User("hello")];

    private static Task<string> Send(StubHttpHandler handler, bool requireJson = false, AiSettings? settings = null) =>
        new OpenAiCompatibleChatClient(new HttpClient(handler), settings ?? Settings)
            .CompleteAsync(Messages, requireJson, CancellationToken.None);

    #region Sending requests

    [Fact]
    public async Task Posts_an_openai_compatible_request_and_returns_the_message_content()
    {
        StubHttpHandler handler = StubHttpHandler.Json(HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"{\"operation\":\"balance\"}"}}]}""");

        string content = await Send(handler, requireJson: true);

        Assert.Equal("""{"operation":"balance"}""", content);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(Settings.ChatCompletionsUri, handler.LastRequest.RequestUri);
        Assert.Equal("Bearer test-key", handler.LastRequest.Headers.Authorization!.ToString());

        JsonNode body = JsonNode.Parse(handler.LastRequestBody!)!;
        Assert.Equal("test-model", body["model"]!.GetValue<string>());
        Assert.Equal("json_object", body["response_format"]!["type"]!.GetValue<string>());
        Assert.Equal("user", body["messages"]![1]!["role"]!.GetValue<string>());
        Assert.Equal("hello", body["messages"]![1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Plain_text_requests_do_not_ask_for_json()
    {
        StubHttpHandler handler = StubHttpHandler.Json(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"Hi there"}}]}""");

        await Send(handler);

        Assert.Null(JsonNode.Parse(handler.LastRequestBody!)!["response_format"]);
    }

    #endregion

    #region Errors are short, readable, and never retried

    [Fact]
    public async Task Rejected_key_gives_an_understandable_error_without_echoing_provider_text()
    {
        StubHttpHandler handler = StubHttpHandler.Json(HttpStatusCode.Unauthorized,
            """{"error":{"message":"Incorrect API key provided: sk-abc***xyz"}}""");

        AiServiceException error = await Assert.ThrowsAsync<AiServiceException>(() => Send(handler));

        Assert.Equal("the AI service didn't accept the API key", error.Problem);
        Assert.Equal("Check that the key is correct and still active, then restart the app with the right key.",
            error.Solution);
        Assert.Equal("HTTP 401", error.Details);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Unknown_model_points_to_the_endpoint_and_model_settings()
    {
        StubHttpHandler handler = StubHttpHandler.Json(HttpStatusCode.NotFound,
            """{"error":{"message":"The model `test-model` does not exist"}}""");

        AiServiceException error = await Assert.ThrowsAsync<AiServiceException>(() => Send(handler));

        Assert.Equal("the AI service couldn't find the model \"test-model\"", error.Problem);
        Assert.Contains("BANKING_AI_MODEL", error.Solution);
        Assert.Contains("BANKING_AI_ENDPOINT", error.Solution);
        Assert.Equal("HTTP 404: The model `test-model` does not exist", error.Details);
    }

    [Fact]
    public async Task Connection_failure_is_reported_once_without_retrying()
    {
        StubHttpHandler handler = StubHttpHandler.Failing(new HttpRequestException("Name or service not known"));

        AiServiceException error = await Assert.ThrowsAsync<AiServiceException>(() => Send(handler));

        Assert.Equal("the AI service at llm.example.test couldn't be reached", error.Problem);
        Assert.Contains("internet connection", error.Solution);
        Assert.Equal("Name or service not known", error.Details);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Slow_service_times_out_with_an_understandable_error()
    {
        StubHttpHandler handler = new StubHttpHandler(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        AiSettings fast = Settings with { Timeout = TimeSpan.FromMilliseconds(100) };

        AiServiceException error = await Assert.ThrowsAsync<AiServiceException>(() => Send(handler, settings: fast));

        Assert.Equal("the AI service took longer than 0.1 seconds to answer", error.Problem);
        Assert.Contains("BANKING_AI_TIMEOUT_SECONDS", error.Solution);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"content":""}}]}""")]
    [InlineData("<html>Bad gateway</html>")]
    public async Task Malformed_success_responses_are_reported(string body)
    {
        StubHttpHandler handler = StubHttpHandler.Json(HttpStatusCode.OK, body);

        AiServiceException error = await Assert.ThrowsAsync<AiServiceException>(() => Send(handler));

        Assert.Equal("the AI service sent back a response I couldn't read", error.Problem);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "the AI service is busy, or the account has run out of credit")]
    [InlineData(HttpStatusCode.InternalServerError, "the AI service had a problem on its end")]
    [InlineData(HttpStatusCode.BadGateway, "the AI service had a problem on its end")]
    [InlineData(HttpStatusCode.BadRequest, "the AI service couldn't handle the request")]
    public async Task Other_http_errors_say_what_went_wrong(HttpStatusCode status, string expectedProblem)
    {
        StubHttpHandler handler = StubHttpHandler.Json(status, """{"error":{"message":"provider says no"}}""");

        AiServiceException error = await Assert.ThrowsAsync<AiServiceException>(() => Send(handler));

        Assert.Equal(expectedProblem, error.Problem);
        Assert.Equal($"HTTP {(int)status}: provider says no", error.Details);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("")]
    public async Task Server_errors_without_details_still_give_a_readable_message(string body)
    {
        StubHttpHandler handler = StubHttpHandler.Json(HttpStatusCode.InternalServerError, body);

        AiServiceException error = await Assert.ThrowsAsync<AiServiceException>(() => Send(handler));

        Assert.Equal("the AI service had a problem on its end", error.Problem);
        Assert.Equal("HTTP 500: no details", error.Details);
    }

    #endregion
}
