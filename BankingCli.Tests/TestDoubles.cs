using System.Net;
using System.Text.Json;
using BankingCli.NaturalLanguage;

namespace BankingCli.Tests;

/// <summary>A scripted <see cref="IChatClient"/> that records every call and fails on unexpected ones.</summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<Func<string>> responses = new Queue<Func<string>>();

    public List<(IReadOnlyList<ChatMessage> Messages, bool RequireJson)> Calls { get; } = [];

    public FakeChatClient Returns(string content)
    {
        responses.Enqueue(() => content);
        return this;
    }

    public FakeChatClient ReturnsIntent(
        string? operation, string? account = null, string? from = null, string? to = null, string? amount = null,
        bool multiple = false) =>
        Returns(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["account"] = account,
            ["from_account"] = from,
            ["to_account"] = to,
            ["amount"] = amount,
            ["multiple_operations"] = multiple
        }));

    public FakeChatClient Throws(Exception exception)
    {
        responses.Enqueue(() => throw exception);
        return this;
    }

    public Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages, bool requireJson, CancellationToken cancellationToken)
    {
        Calls.Add((messages, requireJson));
        if (responses.Count == 0)
        {
            throw new InvalidOperationException("Unexpected call to the AI service.");
        }

        try
        {
            return Task.FromResult(responses.Dequeue()());
        }
        catch (Exception ex)
        {
            return Task.FromException<string>(ex);
        }
    }
}

/// <summary>An <see cref="HttpMessageHandler"/> that captures the request and returns a canned response.</summary>
internal sealed class StubHttpHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastRequestBody { get; private set; }

    public int RequestCount { get; private set; }

    public static StubHttpHandler Json(HttpStatusCode status, string body) =>
        new StubHttpHandler(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));

    public static StubHttpHandler Failing(Exception exception) =>
        new StubHttpHandler(_ => Task.FromException<HttpResponseMessage>(exception));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return await respond(cancellationToken);
    }
}
