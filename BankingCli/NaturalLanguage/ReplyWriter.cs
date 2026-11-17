using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BankingCli.NaturalLanguage;

/// <summary>Uses the API to explain a simulator result in conversational English.</summary>
internal sealed partial class ReplyWriter(IChatClient chatClient)
{
    public const string SystemPrompt = """
        You are a friendly, professional assistant for a banking simulator with chequing and savings accounts.
        You will receive the customer's request, the simulator command that was run, and the simulator's result.
        Write a short, conversational reply (one or two sentences, plain text, no Markdown) that tells the customer what happened.

        Rules:
        - The simulator result is the only source of truth. Report its outcome, amounts, accounts, and balances exactly as given.
        - Write every dollar amount with a "$" sign and two decimal places, exactly as it appears in the command or result. Do not calculate totals, differences, or any other new figures.
        - If the result starts with "ERROR", the transaction did not happen and no balance changed. Say so plainly, and explain why using only the meaning of the error code that you are given. If the result includes a balance, you may mention it.
        - Do not claim anything the result does not show, and do not offer advice, products, or other services.
        """;

    // What each simulator error code means, so the model never has to guess a reason for a decline.
    private static readonly Dictionary<string, string> ErrorMeanings = new Dictionary<string, string>
    {
        ["INSUFFICIENT_FUNDS"] = "The account the money would come from does not have enough funds. " +
                                 "The balance shown is that account's current balance.",
        ["SAME_ACCOUNT"] = "A transfer must be between two different accounts.",
        ["INVALID_AMOUNT"] = "The amount was not accepted. Amounts must be positive, have at most two decimal " +
                             "places, and be within the simulator's limit.",
        ["BALANCE_LIMIT"] = "The transaction would take the receiving account above the simulator's maximum balance.",
        ["INVALID_ACCOUNT"] = "The account name was not recognized. The accounts are chequing and savings.",
        ["INVALID_ARGUMENTS"] = "The command was not in the expected format.",
        ["UNKNOWN_COMMAND"] = "The command was not recognized."
    };

    public async Task<string> WriteAsync(
        string request, string command, string simulatorResult, CancellationToken cancellationToken)
    {
        StringBuilder prompt = new StringBuilder()
            .AppendLine($"Customer request: {request}")
            .AppendLine($"Command run: {command}")
            .AppendLine($"Simulator result: {simulatorResult}");

        string? errorCode = ErrorCode(simulatorResult);
        if (errorCode is not null)
        {
            prompt.AppendLine($"Meaning of {errorCode}: " +
                              ErrorMeanings.GetValueOrDefault(errorCode, "The simulator declined the request."));
        }

        string reply = (await chatClient.CompleteAsync(
            [ChatMessage.System(SystemPrompt), ChatMessage.User(prompt.ToString())],
            requireJson: false,
            cancellationToken)).Trim();

        if (!MentionsOnlyKnownAmounts(reply, command, simulatorResult))
        {
            throw new AiServiceException(
                "the AI reply mentioned a dollar amount that does not match the simulator's result, so it was discarded.");
        }

        return reply;
    }

    /// <summary>
    /// A light guard against a misreported figure: every dollar amount in the reply must be the requested
    /// amount or a figure that appears in the simulator's result.
    /// </summary>
    internal static bool MentionsOnlyKnownAmounts(string reply, string command, string simulatorResult)
    {
        HashSet<decimal> known = DollarAmounts(simulatorResult).ToHashSet();
        string[] commandTokens = command.Split(' ');
        if (commandTokens[0] != "balance"
            && decimal.TryParse(commandTokens[^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out decimal requested))
        {
            known.Add(requested);
        }

        return DollarAmounts(reply).All(known.Contains);
    }

    private static IEnumerable<decimal> DollarAmounts(string text) =>
        DollarAmountPattern().Matches(text)
            .Select(match => decimal.TryParse(match.Groups[1].Value.Replace(",", string.Empty),
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value)
                ? value
                : -1m);

    private static string? ErrorCode(string simulatorResult)
    {
        if (!simulatorResult.StartsWith("ERROR ", StringComparison.Ordinal))
        {
            return null;
        }

        return simulatorResult["ERROR ".Length..].Split(' ', '|')[0];
    }

    [GeneratedRegex(@"\$\s?(\d[\d,]*(?:\.\d+)?)")]
    private static partial Regex DollarAmountPattern();
}
