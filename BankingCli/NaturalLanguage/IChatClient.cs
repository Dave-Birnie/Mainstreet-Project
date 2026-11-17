namespace BankingCli.NaturalLanguage;

internal sealed record ChatMessage(string Role, string Content)
{
    public static ChatMessage System(string content) => new ChatMessage("system", content);

    public static ChatMessage User(string content) => new ChatMessage("user", content);
}

/// <summary>Sends a chat conversation to a language model and returns the reply text.</summary>
internal interface IChatClient
{
    /// <param name="requireJson">Ask the model to return a JSON object.</param>
    Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, bool requireJson, CancellationToken cancellationToken);
}

/// <summary>A failure talking to the AI service, with a message that is safe to show the user.</summary>
internal sealed class AiServiceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
