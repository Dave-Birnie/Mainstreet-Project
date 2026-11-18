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
/// A problem with the AI service, described for the user: what went wrong, and what to do about it.
/// </summary>
/// <param name="problem">What went wrong, as a short phrase that reads well after a colon,
/// such as "the AI service didn't accept the API key".</param>
/// <param name="solution">What the user can do about it, in one or two sentences.</param>
/// <param name="details">Technical details, such as the HTTP status code, shown by the status command.</param>
/// <param name="innerException">The original error, if there was one.</param>
internal sealed class AiServiceException(
    string problem, string solution, string? details = null, Exception? innerException = null)
    : Exception(problem, innerException)
{
    public string Problem { get; } = problem;

    public string Solution { get; } = solution;

    public string? Details { get; } = details;
}
