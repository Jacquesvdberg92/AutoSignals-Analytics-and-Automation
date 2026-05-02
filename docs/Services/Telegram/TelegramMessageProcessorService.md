# TelegramMessageProcessorService

**Namespace:** `AutoSignals.Services.Telegram`  
**Type:** Scoped service

## Overview
`TelegramMessageProcessorService` is the central processing pipeline for incoming Telegram messages. It receives raw messages from `TelegramUserScannerService`, determines the correct parsing strategy, and produces saved `Signal` records.

## Methods

| Method | Description |
|--------|-------------|
| `ProcessMessageAsync(message, provider, ct)` | Processes a text message: parse → deduplicate → predict → save. |
| `ProcessImageMessageAsync(message, imageBytes, provider, ct)` | Processes an image message using vision AI parsing. |

## Processing Pipeline
```
Incoming Telegram message
  ↓
1. Provider lookup (which channel does this come from?)
  ↓
2. Parse strategy selection:
   a. DynamicSignalParserService (regex rules) — tried first
   b. AiSignalParserService (LLM) — if regex fails and UseAiFallback=true
   c. ImageSignalParserService — if message is an image and UseImageParsing=true
  ↓
3. Signal validation (required fields present?)
  ↓
4. SignalDeduplicationService.IsDuplicateAsync() — discard if duplicate
  ↓
5. Signal saved to database
  ↓
6. SignalPredictionService.GeneratePredictionAsync() — async, non-blocking
  ↓
7. ErrorLogService — any failures logged
```

## Notes
- The parsing cascade (regex → AI → discard) ensures best-effort extraction without noisy data.
- Image processing only triggers if the provider has `UseImageParsing = true`.
- Prediction generation runs in the background and does not block message processing.

## Dependencies
- `DynamicSignalParserService`
- `AiSignalParserService`
- `ImageSignalParserService`
- `SignalDeduplicationService`
- `SignalPredictionService`
- `AutoSignalsDbContext`
- `ErrorLogService`
