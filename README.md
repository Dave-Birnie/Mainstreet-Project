# Banking CLI with natural-language requests

This is Mainstreet's .NET 10 banking simulator ([banking-cli-starter](https://github.com/dpo007/banking-cli-starter) at commit `b7eb68e`), extended so you can type everyday English, such as "Move four dollars from savings to chequing", and get a conversational reply. Both parts use an OpenAI-compatible API.

The original starter is preserved unchanged in the first commit. `BankSession.cs`, the simulator, is untouched. Every balance change still goes through it.

## Build, test, and run

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Run these from the repo root:

```sh
dotnet build
dotnet test                                          # 122 automated tests, no API key or network needed
dotnet run --project BankingCli                      # start the CLI
dotnet run --project BankingCli -- --show-commands   # also show the simulator command and raw result for each English request
```

In Visual Studio 2026, open `BankingCli.sln` and run the `BankingCli` project with F5. Set the environment variables below before starting Visual Studio, or in a launch profile that you don't commit.

## Configure the API endpoint, model, and key

Settings come from environment variables. Nothing is hard-coded and no key is stored in the repo.

| Variable | Required | Default | Purpose |
| --- | --- | --- | --- |
| `BANKING_AI_API_KEY` | Yes, for English requests | none (falls back to `OPENAI_API_KEY`) | API key sent as a Bearer token |
| `BANKING_AI_ENDPOINT` | No | `https://api.openai.com/v1` | Base URL of any OpenAI-compatible API. A full `.../chat/completions` URL also works. |
| `BANKING_AI_MODEL` | No | `gpt-4o-mini` | Model ID. The model must support JSON mode (`response_format: json_object`). |
| `BANKING_AI_TIMEOUT_SECONDS` | No | `30` | Time to wait for each API call |

```sh
# bash / zsh
export BANKING_AI_API_KEY="sk-..."
export BANKING_AI_MODEL="gpt-4o-mini"                     # optional
export BANKING_AI_ENDPOINT="https://api.openai.com/v1"    # optional; e.g. http://localhost:11434/v1 for Ollama
dotnet run --project BankingCli
```

```powershell
# PowerShell
$env:BANKING_AI_API_KEY = "sk-..."
$env:BANKING_AI_MODEL = "gpt-4o-mini"
dotnet run --project BankingCli
```

**Without a key**, the app starts normally. Interactive users see that natural-language requests are off, and all original commands work. With redirected input (`dotnet run ... < commands.txt`), the output is byte-for-byte identical to the original starter.

**API problems** produce a plain explanation, never a stack trace. For example: "the AI service rejected the API key (HTTP 401). Check BANKING_AI_API_KEY", or "could not be reached", "did not respond within 30 seconds", or "HTTP 404. Check BANKING_AI_ENDPOINT and that model ... exists".

## Example inputs

The original commands work exactly as before and never call the API:

| Command | Effect |
| --- | --- |
| `deposit <account> <amount>` | Add money to an account |
| `withdraw <account> <amount>` | Take money out of an account |
| `transfer <from> <to> <amount>` | Move money between accounts |
| `balance [account]` | Show one balance, or both when the account is omitted |
| `help` or `?` | Show the command list |
| `exit` | End the session |

Some English requests:

```text
Put fifty dollars into savings
Withdraw $25 from chequing
Move four dollars from savings to chequing
How much money do I have in both accounts?
What's my savings balance?
Take 20 bucks out of my chequing account
Deposit $20                                        -> asks which account
Deposit $50 into savings and move $20 to chequing  -> asks for one request at a time
```

## How it works

Each line of input goes through these steps (`BankingCli/NaturalLanguage/BankingAssistant.cs`):

1. **Original command?** (`OriginalCommand.cs`) If the input starts with a command word, has that command's exact number of words, and names real accounts, it goes straight to `BankSession.Execute`. There's no API call, and malformed amounts still get the simulator's own errors (`deposit savings -5` → `ERROR INVALID_AMOUNT`). Account names are checked so that English such as "Withdraw $25 from chequing" or "deposit 50 savings" is interpreted instead of failing.
2. **Interpret** (`IntentInterpreter.cs`) The API returns a JSON object with `operation`, `account`, `from_account`, `to_account`, `amount`, and `multiple_operations`. The prompt tells the model to use `null` for anything not stated and to treat the user's text as data, not instructions.
3. **Validate locally** (`IntentValidator.cs`)
   - The app accepts only the four operations and the two account names, requires a positive amount with at most two decimal places, and refuses multiple operations.
   - **An account only counts if the user's own words name it** (`Accounts.cs`: "chequing", "checking", "chq", "savings", and so on). If the model returns an account the user never mentioned, it is treated as missing.
   - Transfers need both accounts named. A missing side is asked for, never filled in, even though there are only two accounts.
   - The command is built from these known values only. No text from the model is ever copied into it.
   - Anything missing gets a specific clarification message, and nothing runs.
4. **Execute once.** The single validated command runs through the unchanged simulator. This is the only place an English request can change a balance.
5. **Explain** (`ReplyWriter.cs`) The API gets the request, the command, the simulator's actual result, and, for declines, the documented meaning of the error code. Its reply is accepted only if every dollar figure in it appears in the command or the result.
6. **Fall back.** If the reply call fails, or the reply fails that check, the app prints the simulator's original result. The command is never run again and nothing is retried.

`OpenAiCompatibleChatClient.cs` uses `HttpClient` directly with no SDK. It makes one POST to `/chat/completions` per call, so any OpenAI-compatible provider works.

## Verification

### Required checks (manual, real API)

These ran on 2026-11-17 against OpenAI `gpt-4o-mini`, on the final code, using `dotnet run --project BankingCli -- --show-commands` in an interactive terminal. The `[command: ...]` and `[result: ...]` lines come from `--show-commands`. Below each transcript is what I checked. The AI's wording varies between runs. The amounts and balances do not, because they come from the simulator.

**Check 1: Successful transfer**

Steps: start the app, fund savings with an original command, ask for a transfer in English, then check balances with an original command.

```text
bank> deposit savings 10
DEPOSIT $10.00 TO savings | savings=$10.00
bank> Move four dollars from savings to chequing
  [command: transfer savings chequing 4.00]
  [result: TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00]
The $4.00 has been successfully transferred from your savings account to your chequing account. Your savings balance is now $6.00, and your chequing balance is $4.00.
bank> balance
chequing=$4.00 | savings=$6.00
```

Observed: "four dollars" became exactly one command, `transfer savings chequing 4.00`. The simulator's balances were savings $6.00 and chequing $4.00, and the reply states the same amount, accounts, and balances. The `balance` command confirms them independently.

**Check 2: Request that needs clarification**

Steps: fund savings, ask for a transfer with no amount, check balances, then send the complete request.

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

Observed: the app named the missing detail (the amount) and ran no command; there is no `[command: ...]` line. Balances stayed at chequing $0.00 and savings $10.00. The complete follow-up request then worked normally.

**Check 3: Failed transaction (insufficient funds)**

Steps: put $10 in chequing, ask to withdraw $25 in English, then check balances.

```text
bank> deposit chequing 10
DEPOSIT $10.00 TO chequing | chequing=$10.00
bank> Withdraw $25 from chequing
  [command: withdraw chequing 25.00]
  [result: ERROR INSUFFICIENT_FUNDS | chequing=$10.00]
The withdrawal of $25 from your chequing account did not go through because there are insufficient funds; your current chequing balance is $10.00.
bank> balance
chequing=$10.00 | savings=$0.00
```

Observed: the simulator declined with `ERROR INSUFFICIENT_FUNDS` and both balances stayed the same. The reply gives the real reason and the real $10.00 balance. It doesn't claim the withdrawal happened or invent another cause.

### Additional manual checks

These were run the same way, on the final code, and used the real API unless noted.

| Input | Observed result |
| --- | --- |
| `Put fifty dollars into savings` | Ran `deposit savings 50.00`, and the reply matched the simulator ($50.00 balance). |
| `How much money do I have in both accounts?` | Ran `balance` (read-only): "You have $4.00 in your chequing account and $46.00 in your savings account." |
| `What's my savings balance?` | Ran `balance savings`: "Your savings balance is $46.00." |
| `Take 20 bucks out of my chequing account` | Ran `withdraw chequing 20.00`. The simulator declined (`INSUFFICIENT_FUNDS`, $4.00), and the reply explained it. |
| `Deposit $1,000 into savings` | Ran `deposit savings 1000.00`. |
| `Deposit $20` / `Withdraw 5 dollars` | Asked for the account. No command ran. |
| `Move $4` / `Transfer ten dollars please` | Asked for the from and to accounts. No command ran. |
| `Move $4 to chequing` | Asked for the account to move money from. No command ran. |
| `Transfer $2 from chequing` / `Move $4 out of savings` | Asked for the account to move money to. No command ran. |
| `Transfer $5 from savings to savings` | Ran as stated. The simulator declined (`ERROR SAME_ACCOUNT`), and the reply explained that transfers need two different accounts. |
| `Deposit -5 dollars into savings` / `Deposit $10.555 into chequing` | Rejected: amount must be positive with at most two decimal places. No command ran. |
| `Deposit $50 into savings and move $20 to chequing` | "I can only handle one banking request at a time..." No command ran. |
| `Open a new TFSA for me` | Explained the four supported operations. No command ran. |
| `deposit 50 savings` | Not a valid original command, so it was interpreted and ran `deposit savings 50.00`. |
| `withdraw chequing 1`, `help`, `balance` | Original output, with no API call. |
| No API key, then `Put ten dollars in savings` | `ERROR UNKNOWN_COMMAND`, plus a note that natural-language requests are off because `BANKING_AI_API_KEY` is not set. `deposit savings 10` still worked. |
| Interpretation API failure (`BANKING_AI_ENDPOINT=http://127.0.0.1:9/v1`, nothing listening) | "Sorry, I couldn't process that request because the AI service at 127.0.0.1 could not be reached (Connection refused (127.0.0.1:9)). Nothing has been changed. You can try again, or use a command such as "transfer savings chequing 4"..." `balance` stayed $0.00 / $0.00. |
| Reply API failure after execution (local stub server: interpretation returns 200, reply returns 500) | "I couldn't write a conversational reply because the AI service returned HTTP 500 (simulated outage). Here is the simulator's result:" followed by `DEPOSIT $10.00 TO savings \| savings=$10.00`. `balance` showed savings $10.00, so the deposit ran once. The stub logged exactly two API calls: one interpretation and one reply, with no retry. |

**Original command compatibility:** with no key and redirected input, I piped a 19-line script through both the original starter and this version and compared stdout with `diff`. The output was identical. The script had the README transcript, `help`, `?`, `exit`, a blank line, and malformed commands such as `deposit savings abc`, `help me`, and `exit now`.

### Automated tests

`dotnet test` runs 122 xUnit tests against the real `BankSession`, with a scripted fake in place of the API, in `BankingCli.Tests/`. They cover:

- Original commands give the same output as `BankSession` and make zero API calls.
- Each of the four operations maps to exactly one command.
- Missing, unclear, or unnamed accounts produce clarification messages, including the cases the real model got wrong during testing.
- Negative, zero, three-decimal, word, exponent, and injected amounts are rejected.
- Multiple-operation and unsupported requests are refused.
- An interpretation failure runs nothing.
- A reply failure shows the raw result and changes balances only once.
- A reply with a wrong dollar figure falls back to the raw result.
- The HTTP client sends the right request, gives friendly errors for 401, 404, timeouts, connection failures, and malformed bodies, never retries, and doesn't echo provider text that might contain part of the key.

The three required scenarios also have automated equivalents in `BankingAssistantTests.cs`.

## How I worked

- **Tools:** Claude Code, Anthropic's coding agent, running in a cloud session.
- **How it fit in:** I wrote a requirements plan from Mainstreet's email and gave it to Claude Code, along with the email. It cloned the starter, committed it unchanged as a baseline, and recorded the original program's output for later comparison. It then implemented the feature and wrote the test suite. Finally, it ran the manual checks above against the real API through a pseudo-terminal, so the transcripts are real interactive sessions. I chose the provider and model. The API key went into the cloud environment's secret store, so it was never in the chat, the code, or the repo.
- **A suggestion that was rejected, and how it was checked:** testing against the real model found two problems. "Move $4" was treated as unsupported, and "Move $4 out of savings" asked for an account the user had already named. Claude Code's first fix was a prompt rule telling the model to fill in the other account of a transfer. Re-running the real-API checks showed it made things worse:
  - "Transfer ten dollars please" now **invented** both accounts and ran a transfer. Before the change it had correctly asked.
  - "Transfer $5 from savings to savings" was **rewritten** into savings → chequing, a transaction the user never asked for.

  So the prompt rule was rejected. Prompt wording alone can't guarantee "never invent account information". It was replaced with a deterministic local check: an account counts only if the user's text names it. Claude Code then proposed filling in the other side of a one-account transfer in code, since there are only two accounts. I rejected that too, because the brief asks for missing details to be requested, not inferred. To verify, every manual check was re-run on the final code (the tables above), and unit tests now pin each case (`IntentValidatorTests`).
- **A smaller fix found by tests:** the first version of the HTTP client threw `ArgumentOutOfRangeException` on an empty `choices` array instead of giving a readable error. It was rewritten with explicit `TryGetProperty` checks, and the `Malformed_success_responses_are_reported` tests went from failing to passing.

## Beyond the requirements, and limitations

Small additions, not required by the exercise:

- the `--show-commands` flag
- the dollar-figure check on AI replies
- `BANKING_AI_TIMEOUT_SECONDS`
- the `OPENAI_API_KEY` fallback

No optional enhancements from the brief were built: no history, no multi-turn memory, no offline mode.

Limitations:

- **One request at a time.** There is no conversation memory, so a clarification needs a complete new request.
- **The model still chooses the operation and direction.** Local checks guarantee that every account was named by the user, but not which of two plausible readings was meant. For example, "Move $4 out of savings" was read as a transfer, so it asked where to move the money rather than treating it as a withdrawal.
- **Typos and nicknames for accounts trigger a clarification question.** For example, "savngs" or "rainy-day fund". That's the safe way to be wrong.
- **Amounts aren't checked against the user's text the way accounts are.** The model returned `null` for every request without an amount that I tried, but nothing in the code forces that.
- **The reply check only catches dollar figures.** It verifies figures written with `$`, not amounts written in words. The prompt asks for `$X.XX`.
- **Natural-language requests need a model that supports JSON mode.**
