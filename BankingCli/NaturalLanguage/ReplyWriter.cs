using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// Asks the API to explain a simulator result in conversational English,
/// then checks that the reply doesn't misstate any dollar amounts.
/// </summary>
internal sealed partial class ReplyWriter(IChatClient chatClient)
{
    #region Prompt

    /// <summary>Tells the model that the simulator's result is the only source of truth.</summary>
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

    #endregion

    #region Simulator error codes

    /// <summary>What each simulator error code means, so the model never has to guess why something was declined.</summary>
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

    /// <summary>Returns the code from a result like "ERROR INSUFFICIENT_FUNDS | chequing=$0.00", or null on success.</summary>
    private static string? ErrorCode(string simulatorResult)
    {
        if (!simulatorResult.StartsWith("ERROR ", StringComparison.Ordinal))
        {
            return null;
        }

        return simulatorResult["ERROR ".Length..].Split(' ', '|')[0];
    }

    #endregion

    #region Writing the reply

    /// <summary>Gets a conversational explanation of what the simulator did.</summary>
    /// <param name="request">What the customer typed.</param>
    /// <param name="command">The simulator command that was run.</param>
    /// <param name="simulatorResult">The simulator's actual output.</param>
    /// <param name="cancellationToken">Stops the call early.</param>
    /// <exception cref="AiServiceException">The API failed, or the reply misstated an amount.</exception>
    public async Task<string> WriteAsync(
        string request, string command, string simulatorResult, CancellationToken cancellationToken)
    {
        // Give the model the facts: what was asked, what ran, and what the simulator said.
        StringBuilder facts = new StringBuilder()
            .AppendLine($"Customer request: {request}")
            .AppendLine($"Command run: {command}")
            .AppendLine($"Simulator result: {simulatorResult}");

        string? errorCode = ErrorCode(simulatorResult);
        if (errorCode is not null)
        {
            string meaning = ErrorMeanings.GetValueOrDefault(errorCode, "The simulator declined the request.");
            facts.AppendLine($"Meaning of {errorCode}: {meaning}");
        }

        string reply = await chatClient.CompleteAsync(
            [ChatMessage.System(SystemPrompt), ChatMessage.User(facts.ToString())],
            requireJson: false,
            cancellationToken);
        reply = reply.Trim();

        // Never show a reply with a wrong figure. The caller falls back to the simulator's own result.
        if (!MentionsOnlyKnownAmounts(reply, command, simulatorResult))
        {
            throw new AiServiceException(
                "the AI reply mentioned a dollar amount that does not match the simulator's result, so it was discarded.");
        }

        return reply;
    }

    #endregion

    #region Checking the reply's amounts

    /// <summary>
    /// True when every dollar amount in the reply is either the amount that was requested
    /// or a figure from the simulator's result. "$4" and "$4.00" count as the same.
    /// </summary>
    internal static bool MentionsOnlyKnownAmounts(string reply, string command, string simulatorResult)
    {
        HashSet<decimal> knownAmounts = DollarAmounts(simulatorResult).ToHashSet();

        // The requested amount is the last word of a deposit, withdraw, or transfer command.
        string[] commandWords = command.Split(' ');
        bool hasAmount = commandWords[0] != "balance";
        if (hasAmount && decimal.TryParse(commandWords[^1], NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal requested))
        {
            knownAmounts.Add(requested);
        }

        return DollarAmounts(reply).All(knownAmounts.Contains);
    }

    /// <summary>Finds every "$12.34"-style amount in the text, ignoring thousands separators.</summary>
    private static IEnumerable<decimal> DollarAmounts(string text) =>
        DollarAmountPattern().Matches(text)
            .Select(match => match.Groups[1].Value.Replace(",", string.Empty))
            .Select(digits => decimal.TryParse(digits, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal value)
                ? value
                : -1m); // Can't be read, so it never matches a known amount.

    [GeneratedRegex(@"\$\s?(\d[\d,]*(?:\.\d+)?)")]
    private static partial Regex DollarAmountPattern();

    #endregion
}
