using System.Text;

namespace BankingCli.NaturalLanguage;

/// <summary>
/// Asks the API to explain a simulator result in conversational English, then checks the reply
/// against the real result before it's shown.
/// </summary>
internal sealed class ReplyWriter(IChatClient chatClient)
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

    #region Writing the reply

    /// <summary>Gets a conversational explanation of what the simulator did.</summary>
    /// <param name="request">What the customer typed.</param>
    /// <param name="command">The simulator command that was run.</param>
    /// <param name="simulatorResult">The simulator's actual output.</param>
    /// <param name="cancellationToken">Stops the call early.</param>
    /// <exception cref="AiServiceException">The API failed, or the reply didn't match the simulator's result.</exception>
    public async Task<string> WriteAsync(
        string request, string command, string simulatorResult, CancellationToken cancellationToken)
    {
        // Give the model the facts: what was asked, what ran, and what the simulator said.
        StringBuilder facts = new StringBuilder()
            .AppendLine($"Customer request: {request}")
            .AppendLine($"Command run: {command}")
            .AppendLine($"Simulator result: {simulatorResult}");

        // State the outcome plainly. Without this, the model sometimes called a withdrawal that emptied
        // an account "unsuccessful".
        string? errorCode = SimulatorErrors.Code(simulatorResult);
        if (errorCode is null)
        {
            facts.AppendLine("Outcome: the request succeeded.");
        }
        else
        {
            facts.AppendLine("Outcome: the request was declined, and no balance changed.")
                 .AppendLine($"Meaning of {errorCode}: {SimulatorErrors.MeaningFor(errorCode)}");
        }

        string reply = await chatClient.CompleteAsync(
            [ChatMessage.System(SystemPrompt), ChatMessage.User(facts.ToString())],
            requireJson: false,
            cancellationToken);
        reply = reply.Trim();

        // Never show a reply that disagrees with the simulator. The caller shows the simulator's own result instead.
        string? problem = ReplyCheck.FindProblem(reply, command, simulatorResult);
        if (problem is not null)
        {
            throw new AiServiceException(
                $"the AI's reply didn't match the simulator's result ({problem}), so I didn't show it",
                "Nothing to do. The simulator's own result was shown instead.",
                details: $"Discarded reply: {reply}");
        }

        return reply;
    }

    #endregion
}
