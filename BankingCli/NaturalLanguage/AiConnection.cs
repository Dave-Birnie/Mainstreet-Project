namespace BankingCli.NaturalLanguage;

/// <summary>
/// Whether everyday-English requests are available in this session. If they aren't, it records why and how to fix it.
/// </summary>
/// <param name="Client">The API client, or null when everyday-English requests are off.</param>
/// <param name="Settings">The API settings, shown by the status command.</param>
/// <param name="OffReason">Why everyday-English requests are off.</param>
/// <param name="OffFix">How to turn them on.</param>
internal sealed record AiConnection(IChatClient? Client, AiSettings? Settings, string? OffReason, string? OffFix)
{
    public bool IsOn => Client is not null;

    public static AiConnection On(IChatClient client, AiSettings? settings = null) =>
        new AiConnection(client, settings, OffReason: null, OffFix: null);

    public static AiConnection Off(string reason, string fix) =>
        new AiConnection(Client: null, Settings: null, reason, fix);
}
