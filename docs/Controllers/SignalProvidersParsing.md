# SignalProvidersParsingController

**Authorization:** Admin role only

## Overview
The `SignalProvidersParsingController` is the admin interface for configuring how each signal provider's messages are parsed into structured `Signal` objects. It supports both regex-based rules and AI-assisted parsing.

## Actions

### Index (`GET`)
Lists all signal providers with their current parsing configuration (regex enabled, AI fallback enabled, image parsing enabled).

### Edit (`GET/POST`)
Edit parsing settings for a specific provider including:
- Regex rules (pattern, field mapping)
- AI fallback toggle
- Image parsing toggle + prompt override

### TestRegex (`GET/POST`)
Paste a sample Telegram message and test how the current regex rules parse it. Shows extracted fields and any parse errors.

### GenerateRules (`POST`)
Calls `RegexGeneratorService` to ask the AI to generate regex rules from a set of sample messages. Proposed rules are shown for review before saving.

### ApplyRules (`POST`)
Saves AI-generated regex rules to the database for a specific provider.

## Flow
```
Admin pastes sample messages
  → GenerateRules → RegexGeneratorService → AI generates regex
  → Admin reviews proposed rules
  → ApplyRules saves to DB
  → TelegramMessageProcessorService uses rules on next message
```

## Dependencies
- `AutoSignalsDbContext` — provider settings, regex rules
- `RegexGeneratorService` — AI rule generation
- `DynamicSignalParserService` — tests regex rules against sample messages
