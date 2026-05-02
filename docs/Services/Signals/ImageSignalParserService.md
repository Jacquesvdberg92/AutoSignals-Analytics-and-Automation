# ImageSignalParserService

**Namespace:** `AutoSignals.Services.Signals`  
**Type:** Scoped service

## Overview
`ImageSignalParserService` parses trading signals from images sent in Telegram channels. It uses a vision-capable LLM (e.g. GPT-4o) to extract signal data from screenshots of charts or signal cards.

## Methods

| Method | Description |
|--------|-------------|
| `ParseAsync(imageBytes, mimeType, provider, cancellationToken)` | Sends the image to the vision LLM with an extraction prompt. Returns a parsed `Signal` or `null`. |

## Flow
```
Telegram message contains a photo
  → TelegramMessageProcessorService.ProcessImageMessageAsync()
  → Checks provider.UseImageParsing == true
  → Downloads image bytes from Telegram
  → ImageSignalParserService.ParseAsync(imageBytes, "image/jpeg", provider)
  → Image sent to LLM vision endpoint as base64
  → Extraction prompt includes provider-specific hint (ImageParsingPrompt)
  → Structured JSON response parsed into Signal
```

## Provider Configuration
Each provider can have a custom `ImageParsingPrompt` set in `ProviderSettings`. This lets admins give the model hints about the specific layout/format used by that provider's signal images.

## Notes
- Vision API calls are significantly more expensive than text-only calls.
- Only runs for providers with `UseImageParsing = true`.
- Falls back gracefully: if image parse fails, message is discarded (no silent bad data).

## Dependencies
- LLM Vision API client
- `AutoSignalsDbContext` — provider settings
