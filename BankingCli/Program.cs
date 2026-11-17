using BankingCli;
using BankingCli.NaturalLanguage;

BankSession banking = new BankSession();
bool interactive = !Console.IsInputRedirected;
Action<string>? trace = args.Contains("--show-commands") ? line => Console.WriteLine($"  [{line}]") : null;

BankingAssistant assistant;
if (AiSettings.TryLoad(Environment.GetEnvironmentVariable, out AiSettings? settings, out string problem))
{
    assistant = new BankingAssistant(banking, new OpenAiCompatibleChatClient(new HttpClient(), settings!), trace: trace);
    if (interactive)
    {
        Console.WriteLine($"Natural-language requests are on (model: {settings!.Model}). " +
                          "Type a request such as \"Move $4 from savings to chequing\", or help for commands.");
    }
}
else
{
    string notice = $"Natural-language requests are off: {problem} Original commands still work. See README.md to enable them.";
    assistant = new BankingAssistant(banking, chatClient: null, unavailableNotice: interactive ? notice : null);
    if (interactive)
    {
        Console.WriteLine(notice);
    }
}

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
