namespace BankingCli.NaturalLanguage;

/// <summary>One message in a chat with the model: a role ("system" or "user") and its text.</summary>
internal sealed record ChatMessage(string Role, string Content)
{
    /// <summary>Instructions for the model.</summary>
    public static ChatMessage System(string content) => new ChatMessage("system", content);

    /// <summary>The text the model should respond to.</summary>
    public static ChatMessage User(string content) => new ChatMessage("user", content);
}

/// <summary>Sends a chat to a language model and returns its reply.</summary>
/// <remarks>The app uses <see cref="OpenAiCompatibleChatClient"/>. Tests use a scripted fake.</remarks>
internal interface IChatClient
{
    /// <param name="messages">The chat to send.</param>
    /// <param name="requireJson">True to ask the model to reply with a JSON object.</param>
    /// <param name="cancellationToken">Stops the call early.</param>
    Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, bool requireJson, CancellationToken cancellationToken);
}

/// <summary>
/// A problem talking to the AI service. The message is safe to show the user.
/// It is written to follow "because", for example "because the AI service did not respond".
/// </summary>
internal sealed class AiServiceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
