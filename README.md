# Banking CLI with everyday-English requests

Mainstreet's .NET 10 banking simulator, extended so you can type requests like *"Move four dollars from savings to chequing"* and get a conversational reply. An OpenAI-compatible API interprets each request and writes the reply. Every balance change still goes through the original simulator, `BankSession.cs`, which is unchanged.

## 1. Build and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
dotnet build                      # build
dotnet test                       # run the 208 automated tests (no API key needed)
dotnet run --project BankingCli   # start the app
```

Then type a command or a request at the `bank>` prompt, and type `exit` to quit.

If Visual Studio reports `NETSDK1004`, run `dotnet restore` once. It also helps to keep the project outside OneDrive.

## 2. Configure the API endpoint, model, and key

The app reads its settings from environment variables. **Only the key is required.**

| Setting | Environment variable | Default if not set |
| --- | --- | --- |
| API key | `BANKING_AI_API_KEY` | none |
| API endpoint | `BANKING_AI_ENDPOINT` | `https://api.openai.com/v1` |
| Model | `BANKING_AI_MODEL` | `gpt-4o-mini` |

Set them in the same terminal you run the app from.

**Windows (PowerShell)**
```powershell
$env:BANKING_AI_API_KEY  = "sk-your-key"
$env:BANKING_AI_ENDPOINT = "https://api.openai.com/v1"   # optional
$env:BANKING_AI_MODEL    = "gpt-4o-mini"                 # optional
dotnet run --project BankingCli
```

**macOS / Linux**
```sh
export BANKING_AI_API_KEY="sk-your-key"
export BANKING_AI_ENDPOINT="https://api.openai.com/v1"   # optional
export BANKING_AI_MODEL="gpt-4o-mini"                    # optional
dotnet run --project BankingCli
```

To confirm the settings, type `status` in the app. It shows the endpoint and model in use, and whether the last AI request worked.

**No key set?** The app asks at startup whether you'd like to enter one. The key is hidden as you type and isn't saved. Answer `n` and the original commands still work.

**Other notes:**
- The endpoint can be any OpenAI-compatible API whose model supports JSON mode.
- `OPENAI_API_KEY` also works for the key.
- `BANKING_AI_TIMEOUT_SECONDS` sets the time limit for each API call. The default is 30.
- No key is stored in this repo.

## 3. Example inputs

**Original commands.** These work exactly as before and never call the API.
```text
deposit savings 10
withdraw chequing 1
transfer savings chequing 4
balance
balance savings
help
exit
```

**Everyday English**
```text
Put fifty dollars into savings
Withdraw $25 from chequing
Move four dollars from savings to chequing
transfer 1k from checking to savings
How much money do I have in both accounts?
```

**New command:** `status` shows whether everyday-English requests are on, and how the last AI request went.

## 4. Three checks

I ran these on 2026-11-20 with OpenAI `gpt-4o-mini`. Each check started a fresh session, so both balances began at $0.00. I started the app with `dotnet run --project BankingCli -- --show-commands`. The `--show-commands` flag prints the simulator command each English request turned into, so I could confirm exactly what ran.

The AI's wording changes from run to run. The amounts and balances don't, because they come from the simulator.

### Check 1: Successful transfer

**Steps**
1. Type `deposit savings 10`.
2. Type `Move four dollars from savings to chequing`.
3. Type `balance`.

**Observed results**
1. `DEPOSIT $10.00 TO savings | savings=$10.00`
2. The app ran one simulator command, `transfer savings chequing 4.00`, and replied:
   *"The transfer of $4.00 from your savings account to your chequing account was successful, leaving you with a balance of $6.00 in savings and $4.00 in chequing."*
3. `chequing=$4.00 | savings=$6.00`, which matches the reply.

### Check 2: Request that needs clarification

**Steps**
1. Type `deposit savings 10`.
2. Type `Move some money from savings to chequing` (no amount).
3. Type `balance`.
4. Type `Move $3 from savings to chequing` (the complete request).

**Observed results**
1. `DEPOSIT $10.00 TO savings | savings=$10.00`
2. No command ran. The app replied:
   *"I need a bit more information to make that transfer: the amount. Please include it when you try again, for example "Move $20 from savings to chequing". Nothing has been changed."*
3. `chequing=$0.00 | savings=$10.00`, so nothing changed.
4. The app ran `transfer savings chequing 3.00` and replied:
   *"Your transfer of $3.00 from savings to chequing was successful. Your new savings balance is $7.00, and your chequing balance is $3.00."*

### Check 3: Failure case (not enough money)

**Steps**
1. Type `deposit chequing 10`.
2. Type `Withdraw $25 from chequing`.
3. Type `balance`.

**Observed results**
1. `DEPOSIT $10.00 TO chequing | chequing=$10.00`
2. The app ran `withdraw chequing 25.00`. The simulator declined it with `ERROR INSUFFICIENT_FUNDS | chequing=$10.00`, and the app replied:
   *"Your withdrawal of $25 from your chequing account was declined because there are insufficient funds; your current balance is $10.00."*
3. `chequing=$10.00 | savings=$0.00`, so nothing changed.

### Other checks
- **AI service down before anything ran.** With the endpoint pointed at a closed port, nothing ran. The app explained the problem and how to fix it, and the original commands kept working.
- **AI service down after a command ran.** A test server returned an error for the reply. The app showed the simulator's own result, and the command ran exactly once.
- **Original commands.** With piped input, a script of original commands produced output byte-for-byte identical to the starter app's.
- **Automated tests.** `dotnet test` runs 208 tests against the real simulator, with a fake standing in for the API. They cover every operation, clarifications, invalid amounts, API failures, and the checks on the AI's replies.

## How it works

- **Original commands** and `status` go straight to the simulator, with no API call.
- **English requests:**
  1. The API turns the request into structured JSON.
  2. The app checks it locally:
     - accounts and amounts only count if you actually typed them
     - missing details are asked for, never guessed
  3. The app runs **one** simulator command, built from those checked values.
  4. The API writes the reply from the simulator's real result.
  5. Before the reply is shown, the app checks that its amounts, accounts and outcome match the simulator. If the reply fails that check, or the API fails, the app shows the simulator's own result instead, and the command never runs twice.

## How I worked

- **Tools:** Claude Code (Anthropic) wrote the code, tests and README, and ran the checks against the real API. ChatGPT reviewed the finished work against the brief.
- **Workflow:**
  - I turned the email into a requirements plan.
  - Claude Code built the feature in steps, starting by committing the original starter unchanged.
  - I directed the work and tested it myself. That found two bugs, which were then fixed and tested: a huge amount gave the wrong error message, and "1k" wasn't understood.
- **A suggestion I rejected:** for a transfer that named only one account ("Move $4 to chequing"), Claude Code's version filled in the other account. I rejected that because the brief says to ask for missing details. To verify, I re-ran the request (it now asks which account to move the money from), and unit tests lock that behaviour in.
- **Review findings acted on:** ChatGPT pointed out that a made-up amount could still run, and that a reply could misstate the outcome. Local checks now catch both. The reply check then caught the model calling a successful withdrawal "unsuccessful", which was fixed by telling the model the outcome explicitly.

## Limitations

- **One request at a time.** There's no conversation memory, so after a clarification you send the complete request again.
- **Some wording gets a clarification question instead of a result:** typos in account names, and amounts like "twelve dollars and fifty cents". That's the safe way to be wrong.
- **Beyond the brief:** the `status` command, the startup key prompt, plain-English error explanations, `--show-commands`, and the reply checks.
