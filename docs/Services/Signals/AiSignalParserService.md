# AiSignalParserService

**Namespace:** `AutoSignals.Services.Signals`  
**Type:** Scoped service

## Overview
`AiSignalParserService` uses a large language model (LLM) to parse raw Telegram message text into a structured `Signal` object. It is used as the primary parser for providers configured with AI parsing, and as the fallback when regex parsing fails.

## Methods

| Method | Description |
|--------|-------------|
| `ParseAsync(message, provider, cancellationToken)` | Submits the message text to the LLM with a structured extraction prompt. Returns a parsed `Signal` or `null` if parsing fails. |

## Flow
```
Telegram message received for a provider with AI parsing enabled
  → AiSignalParserService.ParseAsync(messageText, provider)
  → Prompt built: system instructions + provider context + raw message
  → LLM API called (OpenAI / compatible)
  → JSON response parsed into Signal fields:
      symbol, side (long/short), entry, TP1/TP2/TP3, stop-loss, leverage
  → Signal validated and returned
```

## Prompt Strategy
The prompt instructs the LLM to:
1. Extract the trading pair and direction
2. Identify entry price or range
3. Extract take-profit levels in order
4. Extract stop-loss level
5. Return structured JSON — no explanation text

## Notes
- If the LLM returns unparseable JSON, `ParseAsync` returns `null` and the message is discarded.
- Token usage is logged for cost monitoring.
- `UseAiFallback` flag on `ProviderSettings` controls whether this runs after regex failure.

## Dependencies
- LLM API client (OpenAI SDK or compatible)
- `AutoSignalsDbContext` — provider settings lookup
