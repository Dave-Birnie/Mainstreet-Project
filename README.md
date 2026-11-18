# Banking CLI with everyday-English requests

This is Mainstreet's .NET 10 banking simulator ([banking-cli-starter](https://github.com/dpo007/banking-cli-starter) at commit `b7eb68e`), extended so you can type requests like "Move four dollars from savings to chequing" and get a conversational reply. An OpenAI-compatible API interprets each request and writes the reply.

The starter is preserved unchanged in the first commit. `BankSession.cs`, the simulator, is untouched, and every balance change still goes through it.

## Build, test, and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
dotnet build
dotnet test                                          # 191 tests, no API key or network needed
dotnet run --project BankingCli                      # start the CLI
dotnet run --project BankingCli -- --show-commands   # also show the simulator command behind each English request
```

## Configure the API

| Variable | Default | Purpose |
| --- | --- | --- |
| `BANKING_AI_API_KEY` | none (falls back to `OPENAI_API_KEY`) | API key |
| `BANKING_AI_ENDPOINT` | `https://api.openai.com/v1` | Any OpenAI-compatible base URL, e.g. `http://localhost:11434/v1` for Ollama |
| `BANKING_AI_MODEL` | `gpt-4o-mini` | Model ID. It must support JSON mode. |
| `BANKING_AI_TIMEOUT_SECONDS` | `30` | Time limit for each API call |

```sh
export BANKING_AI_API_KEY="sk-..."      # PowerShell: $env:BANKING_AI_API_KEY = "sk-..."
dotnet run --project BankingCli
```

If no key is set, the app asks whether you'd like to enter one. The key is hidden as you type, used for that session only, and never saved. Answer `n` and the original commands work as before. No key is stored in the code or the repo.

## Example inputs

**Original commands** work exactly as before and never call the API: `deposit savings 10`, `withdraw chequing 1`, `transfer savings chequing 4`, `balance`, `balance savings`, `help` or `?`, and `exit`.

**New:** `status` shows whether everyday-English requests are on, the model and endpoint, and how the last AI call went.

**Everyday English:**

```text
Put fifty dollars into savings
Withdraw $25 from chequing
Move four dollars from savings to chequing
How much money do I have in both accounts?
Move some money to chequing            -> asks for the amount and the account to move it from
Deposit $50 and move $20 to chequing   -> asks for one request at a time
```

## How it works

1. **Original commands and `status`** are handled directly, with no API call.
2. **Interpret.** The API returns JSON: the operation, the accounts, the amount, and whether there were multiple operations.
3. **Validate locally.**
   - Only the four operations and the two accounts are accepted.
   - **Accounts and amounts only count if the user's own words contain them**, in digits or words.
   - Amounts must be positive, with at most two decimal places.
   - Anything missing gets a clarification question, and nothing runs.
   - The command is built from these checked values, never from the model's text.
4. **Run once** through the unchanged simulator.
5. **Explain.** The API turns the real result into a reply, which is checked before it's shown:
   - every dollar figure must come from the result
   - each balance must be on the right account
   - a declined request must be described as declined
6. **Fall back.** If the reply call fails or a check fails, the simulator's own result is shown, with a plain-English explanation if it was declined. The command is never run again.

Errors say what went wrong and what to do about it. For example, with a wrong API key:

```text
Sorry, I couldn't process that request: the AI service didn't accept the API key. Nothing has been changed.
To fix this: Check that the key is correct and still active, then restart the app with the right key. You can still use commands such as "transfer savings chequing 4" (type help for the list).
```

## Verification

The three required checks ran on 2026-11-18 against OpenAI `gpt-4o-mini`, using `dotnet run --project BankingCli -- --show-commands`. The `[command]` and `[result]` lines come from `--show-commands`. The AI's wording varies between runs. The amounts and balances don't, because they come from the simulator.

**1. Successful transfer.** Fund savings, ask for a transfer in English, then check balances.

```text
bank> deposit savings 10
DEPOSIT $10.00 TO savings | savings=$10.00
bank> Move four dollars from savings to chequing
  [command: transfer savings chequing 4.00]
  [result: TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00]
The transfer of $4.00 from your savings account to your chequing account was successful. Your savings balance is now $6.00, and your chequing balance is $4.00.
bank> balance
chequing=$4.00 | savings=$6.00
```

Observed: exactly one command ran. The reply matches the simulator's balances, and `balance` confirms them.

**2. Clarification.** Ask for a transfer without an amount, check balances, then send the complete request.

```text
bank> deposit savings 10
DEPOSIT $10.00 TO savings | savings=$10.00
bank> Move some money from savings to chequing
I need a bit more information to make that transfer: the amount. Please include it when you try again, for example "Move $20 from savings to chequing". Nothing has been changed.
bank> balance
chequing=$0.00 | savings=$10.00
bank> Move $3 from savings to chequing
  [command: transfer savings chequing 3.00]
  [result: TRANSFER $3.00 FROM savings TO chequing | savings=$7.00 | chequing=$3.00]
The transfer of $3.00 from your savings account to your chequing account was successful. Your savings balance is now $7.00, and your chequing balance is $3.00.
```

Observed: the app asked for the amount and ran nothing (there is no `[command]` line). The balances didn't change, and the complete request then worked.

**3. Declined transaction.** Try to withdraw more than chequing holds.

```text
bank> deposit chequing 10
DEPOSIT $10.00 TO chequing | chequing=$10.00
bank> Withdraw $25 from chequing
  [command: withdraw chequing 25.00]
  [result: ERROR INSUFFICIENT_FUNDS | chequing=$10.00]
The withdrawal of $25 from your chequing account did not occur because there are insufficient funds; your current balance is $10.00.
bank> balance
chequing=$10.00 | savings=$0.00
```

Observed: the simulator declined the withdrawal and the balances didn't change. The reply gives the real reason and the real balance.

**Other manual checks** (same setup):

- **Covered:**
  - all four operations
  - requests with more than one operation, and unsupported requests
  - negative and three-decimal amounts
  - transfers that name one account: "Move $4 to chequing" asks for the source account
  - "savings to savings", which the simulator declines under its own rule
- **API failure before anything runs:** with the endpoint pointed at a closed port, nothing ran. The message explained the problem and the fix, and `status` showed the technical details.
- **API failure after the command ran:** a local stub returned HTTP 500 for the reply call. The simulator's result was shown, the deposit ran once, and the stub logged exactly two calls.
- **No API key:** answering `y` at the startup prompt and typing a key turned everyday English on. Answering `n` left the original commands working, and `help` and `status` explained how to turn it on.
- **Original commands:** with piped input, a 19-line script of original commands, including malformed ones, produced output byte-for-byte identical to the starter's.

**Automated tests:** `dotnet test` runs 191 xUnit tests against the real simulator, with a scripted fake standing in for the API. They cover:

- routing, and compatibility with the original commands
- validation, including accounts and amounts the model invented
- every failure path
- the HTTP client's error handling
- the reply checks, run against all 23 replies `gpt-4o-mini` wrote during manual testing (all accepted) and against misleading replies they must catch

## How I worked

- **Tools:** Claude Code (Anthropic) wrote the code, tests, and README in a cloud session, and ran the checks against the real API. ChatGPT reviewed the finished work against the brief.
- **Workflow:**
  - I wrote a requirements plan from the email and gave it to Claude Code, along with the email.
  - I made the calls on the provider and model, on how the app behaves (the startup key prompt, `help` and `status`, plain-English errors), and on which review findings to act on.
  - Claude Code committed the starter unchanged first and recorded its output for comparison.
  - The API key stayed in the cloud environment's secret store, never in the chat, the code, or the repo.
- **A suggestion I rejected:** Claude Code's version filled in the other account when a transfer named only one ("Move $4 to chequing" became from savings), since there are only two accounts. I rejected that because the brief asks for missing details to be requested, not filled in. To verify, the real-API checks were re-run, and the request now asks which account to move the money from. Unit tests pin the behaviour.
- **Review findings acted on:** ChatGPT pointed out two gaps:
  - An amount the model invented could still run.
  - A reply could attach correct figures to the wrong account, or call a declined request successful.

  Both are now caught by local checks. The fix was verified with unit tests that include every real model reply, all still accepted, and by re-running every manual check.

## Limitations, and what's beyond the brief

- **One request at a time.** There's no conversation memory, so after a clarification question the user sends a complete new request.
- **The model still chooses the operation and direction.** "Move $4 out of savings" is read as a transfer, so it asks where the money should go, rather than as a withdrawal.
- **Some wording gets a clarification question instead of a result:** typos or nicknames for accounts, and amounts like "twelve dollars and fifty cents". That's the safe way to be wrong.
- **The reply checks have limits.** They catch wrong figures, balances on the wrong account, and the wrong outcome, but not every possible misstatement in free text.
- **Additions beyond the brief:**
  - the `status` command
  - the startup key prompt
  - the extra help text and plain-English error explanations, in interactive sessions only, so piped output is unchanged
  - `--show-commands`
  - the reply checks
