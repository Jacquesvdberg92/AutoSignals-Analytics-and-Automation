# RegexGeneratorService

**Namespace:** `AutoSignals.Services.Signals`  
**Type:** Scoped service

## Overview
`RegexGeneratorService` uses an LLM to automatically generate regex parsing rules from a set of sample Telegram messages. The generated rules can then be reviewed by an admin and saved for a provider via `SignalProvidersParsingController`.

## Methods

| Method | Description |
|--------|-------------|
| `GenerateRulesAsync(sampleMessages, provider, cancellationToken)` | Submits sample messages to the LLM and returns a list of proposed `ProviderRegexRule` objects. |

## Flow
```
Admin pastes 3-5 sample messages in parsing config
  → RegexGeneratorService.GenerateRulesAsync(samples, provider)
  → Prompt: "Given these messages, generate named-group regex rules to extract
     symbol, direction, entry, TP1, TP2, TP3, SL, leverage"
  → LLM returns JSON array of regex rules
  → Rules parsed and presented to admin for review
  → Admin clicks Apply → rules saved to ProviderSettings
  → DynamicSignalParserService uses new rules going forward
```

## Notes
- Generated rules are proposals only — they are not auto-saved without admin approval.
- Quality varies with message variety; providing diverse samples produces better rules.
- After saving, use the TestRegex tool to validate against additional messages.

## Dependencies
- LLM API client
