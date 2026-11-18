using BankingCli;
using BankingCli.NaturalLanguage;

#region Setup

BankSession banking = new BankSession();
bool interactive = !Console.IsInputRedirected;
bool showCommands = args.Contains("--show-commands");

BankingAssistant assistant = CreateAssistant(banking, interactive, showCommands);

#endregion

#region Read, run, print (the starter's loop, now calling the assistant)

while (true)
{
    // Keep redirected output limited to command results so it can be compared line by line.
    if (interactive)
    {
        Console.Write("bank> ");
    }

    string? input = Console.ReadLine();
    if (input is null)
    {
        break;
    }

    CommandResult result = await assistant.HandleAsync(input);
    if (result.Output.Length > 0)
    {
        Console.WriteLine(result.Output);
    }

    if (result.ShouldExit)
    {
        break;
    }
}

#endregion

#region Helpers

// Turns on natural-language requests when an API key is configured.
// Without one, the app behaves exactly like the original starter.
static BankingAssistant CreateAssistant(BankSession banking, bool interactive, bool showCommands)
{
    if (!AiSettings.TryLoad(Environment.GetEnvironmentVariable, out AiSettings? settings, out string problem))
    {
        string notice = $"Natural-language requests are off: {problem} Original commands still work. " +
                        "See README.md to enable them.";
        if (interactive)
        {
            Console.WriteLine(notice);
        }

        return new BankingAssistant(banking, chatClient: null, unavailableNotice: interactive ? notice : null);
    }

    if (interactive)
    {
        Console.WriteLine($"Natural-language requests are on (model: {settings!.Model}). " +
                          "Type a request such as \"Move $4 from savings to chequing\", or help for commands.");
    }

    // --show-commands prints the simulator command and raw result under each English request.
    Action<string>? trace = showCommands ? line => Console.WriteLine($"  [{line}]") : null;

    OpenAiCompatibleChatClient chatClient = new OpenAiCompatibleChatClient(new HttpClient(), settings!);
    return new BankingAssistant(banking, chatClient, trace: trace);
}

#endregion
