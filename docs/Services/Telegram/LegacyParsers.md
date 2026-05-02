# Telegram Signal Parsers

**Namespace:** `AutoSignals.Services.Telegram.Parsers`  
**Type:** Static / scoped parsers (one per legacy provider)

## Overview
The `Parsers/` folder contains a set of **hardcoded, provider-specific** signal parsers. These were written before the dynamic regex/AI system was introduced and remain for backward compatibility with providers that have unique, stable message formats.

## Parser List

| Class | Provider |
|-------|----------|
| `AlexFredmanSignalParser` | Alex Fredman signals channel |
| `AlwaysWinSignalParser` | Always Win channel |
| `BinanceMasterSignalParser` | Binance Master signals |
| `BybitProSignalParser` | Bybit Pro channel |
| `CoinCoachSignalParser` | CoinCoach signals |
| `CryptoAmanSignalParser` | Crypto Aman channel |
| `CryptoAndrewSignalParser` | Crypto Andrew channel |
| `CryptoInnerCircleSignalParser` | Crypto Inner Circle |
| `FedRussianInsiderSignalParser` | Fed Russian Insider |
| `Scalping300SignalParser` | Scalping 300 channel |
| `WolfXSignalParser` | WolfX signals |

## Notes
- These parsers are **not recommended** for new providers. Use the dynamic regex/AI system instead (`SignalProvidersParsingController` → `DynamicSignalParserService`).
- Each parser implements a `TryParse(messageText)` method that returns a `Signal` or `null`.
- These are only invoked by `TelegramMessageProcessorService` if the provider is still mapped to a legacy parser and has no dynamic rules configured.
