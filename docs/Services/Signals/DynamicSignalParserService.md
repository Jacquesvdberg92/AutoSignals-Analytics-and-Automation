# DynamicSignalParserService

**Namespace:** `AutoSignals.Services.Signals`  
**Type:** Scoped service

## Overview
`DynamicSignalParserService` parses raw Telegram message text into a `Signal` using the regex rules stored in `ProviderSettings`. Each provider has a set of named capture-group rules that map to signal fields (symbol, side, entry, TP1–TP3, SL, leverage).

## Methods

| Method | Description |
|--------|-------------|
| `ParseAsync(message, provider)` | Applies the provider's regex rules to the message text. Returns a `Signal` on success, or `null` if no rule matches. |
| `TestParse(message, rules)` | Tests a set of rules against a message without saving. Returns field extraction results for each rule. Used in the admin parsing tester. |

## Flow
```
Telegram message received
  → Load ProviderSettings.RegexRules for provider
  → For each rule (ordered by priority):
      → Apply regex to message
      → Extract named capture groups
      → Map groups to Signal fields
  → If all required fields (symbol, side, entry) extracted → return Signal
  → Else → return null (triggers AI fallback if enabled)
```

## Rule Structure
Each rule has:
- `Pattern` — C# regex with named groups e.g. `(?P<symbol>[A-Z]+USDT)`
- `FieldMapping` — dictionary of capture group name → signal field
- `Priority` — ordering when multiple rules defined

## Dependencies
- `AutoSignalsDbContext` — loads provider regex rules
