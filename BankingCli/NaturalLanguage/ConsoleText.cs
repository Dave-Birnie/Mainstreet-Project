using System.Text;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// The app's own text: the welcome line, the extra help, and the status report.
/// </summary>
internal static class ConsoleText
{
    private static readonly string NewLine = Environment.NewLine;

    #region Welcome

    /// <summary>The first line interactive users see.</summary>
    public static string Welcome(AiConnection connection) => connection.IsOn
        ? $"Everyday-English requests are on (model: {connection.Settings?.Model}). " +
          "Type a request such as \"Move $4 from savings to chequing\", or help for commands."
        : $"Everyday-English requests are off. {connection.OffReason} Original commands still work. " +
          "Type help to see them, or status to see how to turn everyday English on.";

    #endregion

    #region Help

    /// <summary>
    /// Added after the original help text in interactive sessions.
    /// The original list is left exactly as it was.
    /// </summary>
    public static string MoreHelp(AiConnection connection)
    {
        StringBuilder text = new StringBuilder()
            .Append("status").Append(NewLine)
            .Append(NewLine);

        if (connection.IsOn)
        {
            text.Append("You can also type what you want in everyday English, one request at a time. For example:").Append(NewLine)
                .Append("  Put $50 into savings").Append(NewLine)
                .Append("  Move $20 from savings to chequing").Append(NewLine)
                .Append("  How much do I have in both accounts?");
        }
        else
        {
            text.Append("Everyday-English requests are off. Type status to see why, and how to turn them on.");
        }

        return text.ToString();
    }

    #endregion

    #region Status

    /// <summary>Whether everyday-English requests are on, how they're set up, and how the last AI call went.</summary>
    /// <param name="connection">How the AI is set up for this session.</param>
    /// <param name="hasCalledAi">True once at least one AI call has been made.</param>
    /// <param name="lastProblem">The problem with the most recent AI call, or null if it worked.</param>
    public static string Status(AiConnection connection, bool hasCalledAi, AiServiceException? lastProblem)
    {
        if (!connection.IsOn)
        {
            return $"Everyday-English requests: off{NewLine}" +
                   $"  Why:     {connection.OffReason}{NewLine}" +
                   $"  To fix:  {connection.OffFix}{NewLine}" +
                   "Original commands work either way. Type help to see them.";
        }

        StringBuilder text = new StringBuilder().Append("Everyday-English requests: on").Append(NewLine);

        AiSettings? settings = connection.Settings;
        if (settings is not null)
        {
            text.Append($"  Model:     {settings.Model}").Append(NewLine)
                .Append($"  Endpoint:  {settings.ChatCompletionsUri}").Append(NewLine)
                .Append($"  API key:   {settings.ApiKeySource}").Append(NewLine)
                .Append($"  Timeout:   {settings.Timeout.TotalSeconds:0.##} seconds").Append(NewLine);
        }

        text.Append("  Last AI request: ");
        if (!hasCalledAi)
        {
            text.Append("none yet");
        }
        else if (lastProblem is null)
        {
            text.Append("worked");
        }
        else
        {
            text.Append($"failed because {lastProblem.Problem}.").Append(NewLine)
                .Append($"    To fix:  {lastProblem.Solution}");
            if (lastProblem.Details is not null)
            {
                text.Append(NewLine).Append($"    Details: {lastProblem.Details}");
            }
        }

        return text.ToString();
    }

    #endregion
}
