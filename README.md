# Banking CLI with everyday-English requests

Mainstreet's .NET 10 banking simulator, extended so you can type requests like *"Move four dollars from savings to chequing"* and get a conversational reply. An OpenAI-compatible API interprets each request and writes the reply. Every balance change still goes through the original simulator, `BankSession.cs`, which is unchanged.

## 1. Build and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
dotnet build                      # build
dotnet test                       # run the 271 automated tests (no API key needed)
dotnet run --project BankingCli   # start the app; type exit to quit
```

If Visual Studio reports `NETSDK1004`, run `dotnet restore` once. It also helps to keep the project outside OneDrive.

## 2. Configure the API endpoint, model, and key

Settings come from environment variables. **Only the key is required.**

| Setting | Environment variable | Default |
| --- | --- | --- |
| API key | `BANKING_AI_API_KEY` | none |
| API endpoint | `BANKING_AI_ENDPOINT` | `https://api.openai.com/v1` |
| Model | `BANKING_AI_MODEL` | `gpt-4o-mini` |

**Windows (PowerShell)**
```powershell
$env:BANKING_AI_API_KEY  = "sk-your-key"
$env:BANKING_AI_ENDPOINT = "https://api.openai.com/v1"   # optional
$env:BANKING_AI_MODEL    = "gpt-4o-mini"                 # optional
dotnet run --project BankingCli
```

**macOS / Linux:** the same, using `export BANKING_AI_API_KEY="sk-your-key"` and so on.

**Google Gemini:** set the endpoint to `https://generativelanguage.googleapis.com/v1beta/openai`, the model to `gemini-3.8-flash`, and the key to a Gemini API key.

**Other OpenAI-compatible APIs:** set all three variables. The model must support JSON mode.

**No key set?** The app asks at startup whether you'd like to enter a Gemini or ChatGPT key, and sets the matching endpoint and model. The key is hidden as you type and isn't saved. Answer `n` and the original commands still work.

Type `status` in the app to see the endpoint and model in use, and whether the last AI request worked. No key is stored in this repo.

## 3. Example inputs

```text
deposit savings 10                           (original commands work exactly as before)
transfer savings chequing 4
balance
Put fifty dollars into savings               (everyday English)
transfer 1k from checking to savings
How much money do I have in both accounts?
status                                       (new: shows the AI connection)
```

## 4. Three checks

I ran these on 2026-11-20 with OpenAI `gpt-4o-mini`, starting each in a fresh session with both balances at $0.00. I repeated them with Google Gemini `gemini-3.8-flash`, which gave the same commands and balances. I started the app with `dotnet run --project BankingCli -- --show-commands`, which prints the simulator command behind each English request. The AI's wording varies from run to run. The amounts and balances come from the simulator, so they don't.

### Check 1: Successful transfer

**Steps:** type `deposit savings 10`, then `Move four dollars from savings to chequing`, then `balance`.

**Observed:**
1. `DEPOSIT $10.00 TO savings | savings=$10.00`
2. One command ran, `transfer savings chequing 4.00`. The reply: *"The transfer of $4.00 from your savings account to your chequing account was successful! Your savings balance is now $6.00, and your chequing balance is $4.00."*
3. `chequing=$4.00 | savings=$6.00`, which matches the reply.

### Check 2: Request that needs clarification

**Steps:** type `deposit savings 10`, then `Move some money from savings to chequing` (no amount), then `balance`, then `Move $3 from savings to chequing`.

**Observed:**
1. `DEPOSIT $10.00 TO savings | savings=$10.00`
2. No command ran. The reply: *"I need a bit more information to make that transfer: the amount. Please include it when you try again, for example "Move $20 from savings to chequing". Nothing has been changed."*
3. `chequing=$0.00 | savings=$10.00`, so nothing changed.
4. `transfer savings chequing 3.00` ran. The reply: *"The transfer of $3.00 from your savings account to your chequing account was successful, leaving you with a balance of $7.00 in savings and $3.00 in chequing."*

### Check 3: Failure case (not enough money)

**Steps:** type `deposit chequing 10`, then `Withdraw $25 from chequing`, then `balance`.

**Observed:**
1. `DEPOSIT $10.00 TO chequing | chequing=$10.00`
2. `withdraw chequing 25.00` ran and the simulator declined it with `ERROR INSUFFICIENT_FUNDS | chequing=$10.00`. The reply: *"Your request to withdraw $25 from your chequing account was declined because there are insufficient funds; the current balance is $10.00."*
3. `chequing=$10.00 | savings=$0.00`, so nothing changed.

### Other checks
- **API failures:** with the endpoint pointed at a closed port, nothing ran and the app explained how to fix it. When a test server failed only the reply, the app showed the simulator's own result, and the command ran exactly once.
- **Original commands:** with piped input, a script of original commands produced output identical to the starter app's.
- **Automated tests:** 271 tests run against the real simulator, with a fake standing in for the API. They include every reply the two models wrote during testing (all accepted), and misleading replies that must be rejected.

## How it works

1. Original commands and `status` go straight to the simulator, with no API call.
2. For English requests, the API returns structured JSON. The app then checks it locally:
   - Accounts and amounts must appear in what you typed.
   - Missing details are asked for.
   - A request with two different amounts ("$5 or $10") is asked about, not guessed.
   - A deposit or withdrawal that names both accounts is asked about too.
   - A transfer must match your own "from" and "to" words.
3. The app runs **one** simulator command, built from the checked values.
4. The API writes the reply from the simulator's real result. Before it's shown, the app checks the reply's amounts, balances, direction, type of transaction, outcome and decline reason against that result. If the check fails, or the API fails, the app shows the simulator's own result instead. The command never runs twice.

## How I worked

- **Tools:** Claude Code (Anthropic) wrote the code, tests and README, and ran the checks against the real APIs. ChatGPT reviewed the work against the brief.
- **Workflow:**
  1. **I planned the work.** I spent about 45 minutes turning the email into a written requirements plan.
  2. **Claude Code built it from that plan.**
  3. **I spent about 2 hours testing and reviewing the code.** My testing found that a huge amount gave the wrong error, and that "1k" wasn't understood. Both were fixed.
- **A suggestion I accepted:** I considered adding transaction history and a confirmation step for large transfers. Claude Code advised against it, because neither is in the brief, history would need changes to the simulator, and confirmation would add a banking rule Mainstreet hadn't asked for. I agreed and kept the scope to the assignment.
- **A suggestion I rejected:** for a transfer naming only one account ("Move $4 to chequing"), Claude Code's version filled in the other account. I rejected that because the brief says to ask for missing details. To verify, I re-ran the request (it now asks which account to use), and unit tests lock that behaviour in.
- **Reviews acted on:** three reviews fed the app deliberately wrong model output through a test server. They found replies the checker let through (a wrong balance, a reversed direction, "I withdrew" after a transfer, an invented reason for a decline) and requests where the model could pick an amount or account. Each case is now a regression test. Re-running every check on both real models produced no false alarms.

## Limitations

- **One request at a time.** There's no conversation memory, so after a clarification you send the complete request again.
- **The checks confirm that an amount was typed, not that it was meant.** "My savings goal is $100. Deposit some money" relies on the model leaving the amount out. Both models did in testing, but that isn't guaranteed.
- **The reply checks are word-based.** They catch wrong figures, balances on the wrong account, the wrong direction, and the wrong outcome, but free text can't be verified completely. When in doubt they reject the reply and show the simulator's own result.
- **Beyond the brief:** the `status` command, the startup key prompt, plain-English error explanations, `--show-commands`, and the reply checks.
