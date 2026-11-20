using BankingCli;
using BankingCli.NaturalLanguage;

#region Setup

BankSession banking = new BankSession();
bool interactive = !Console.IsInputRedirected;

// --show-commands prints the simulator command and raw result under each English request.
Action<string>? trace = args.Contains("--show-commands") ? line => Console.WriteLine($"  [{line}]") : null;

AiConnection connection = ConnectToAi(interactive);
BankingAssistant assistant = new BankingAssistant(banking, connection, interactive, trace);

if (interactive)
{
    Console.WriteLine(ConsoleText.Welcome(connection));
}

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

// Turns on everyday-English requests when an API key is available.
// With no key, interactive users are offered the chance to enter one. Otherwise the app works like the original.
static AiConnection ConnectToAi(bool interactive)
{
    Func<string, string?> getVariable = Environment.GetEnvironmentVariable;
    string? keySource = null;

    if (!AiSettings.HasApiKey(getVariable))
    {
        EnteredKey? entered = interactive ? ApiKeyPrompt.Ask() : null;
        if (entered is null)
        {
            return AiConnection.Off(
                "No API key is set.",
                $"Restart the app and enter a key when asked, or set {AiSettings.ApiKeyVariable} before starting it.");
        }

        // Use the typed key, with the chosen provider's endpoint and model, for this session only.
        getVariable = ApiKeyPrompt.SettingsWith(entered, getVariable);
        keySource = $"entered at startup for {entered.Provider.Name} (not saved)";
    }

    if (!AiSettings.TryLoad(getVariable, out AiSettings? settings, out string problem))
    {
        return AiConnection.Off(problem, "Then restart the app.");
    }

    if (keySource is not null)
    {
        settings = settings! with { ApiKeySource = keySource };
    }

    return AiConnection.On(new OpenAiCompatibleChatClient(new HttpClient(), settings!), settings);
}

#endregion
