# TelegramBotService – Functionality Summary

## Overview
`TelegramBotService` is a background service that operates a Telegram bot. Its core responsibilities include:

- Listening for and processing Telegram messages.
- Parsing crypto trading signals from multiple providers.
- Validating and saving these signals into the database.
- Triggering automated order creation for active users.
- Sending messages, images, and error logs to specific Telegram groups.

---

## Key Components

### Dependencies
The service relies on:

- **ITelegramBotClient** – sends/receives Telegram messages  
- **IServiceScopeFactory** – resolves scoped services like DbContext  
- **ILogger** – logging  
- **TelegramGroupsOptions** – contains Telegram group IDs

### Signal Caches
Every provider has its own `ConcurrentDictionary<string, Queue<Signal>>` to store the **last 3 signals**, preventing duplicates.

Providers include:

- WolfX  
- Alex Fredman  
- Russian Insider  
- CoinCoach  
- Scalping300  
- Binance Masters  
- BybitPro  
- Crypto Andrew  
- Crypto Inner Circle  
- Crypto Aman  
- AlwaysWin  

---

## Bot Execution Flow

### Starting the Bot
The service starts a long-running listener using:

```
StartReceiving(HandleUpdateAsync, HandleErrorAsync)
```

This handles all incoming Telegram updates.

---

## Message Processing Logic

### 1. Skip Old Messages  
Messages older than **60 minutes** are ignored.

### 2. Private Chats  
If a user writes privately, the bot responds with:

```
Coming soon — visit AutoSignals.xyz
```

No parsing is performed in private chats.

### 3. Extract Message Content  
The bot reads:

- `Message.Text`, or  
- `Message.Caption` if the message is a photo  

### 4. Parse Signal  
The bot executes multiple parser functions in order:

- ParseBybitPro  
- ParseBinanceMaster  
- ParseAlexFredman  
- ParseScalping300  
- ParseCoinCoach  
- ParseFedRussianInsider  
- ParseWolfX  
- ParseCryptoAndrew  
- ParseCryptoInnerCircle  
- ParseCryptoAman  
- ParseAlwaysWin  

The first parser that returns a non-null `Signal` is used.

---

## Signal Saving Process

When a valid `Signal` is parsed:

1. The bot fetches the symbol's **general price** from the database.  
2. Ensures the signal’s entry price is within **5%** of the general price.  
3. Saves the `Signal` to the database.  
4. Generates a corresponding `SignalPerformance` record.  
5. Logs success.

If validation fails, the signal is not stored.

---

## Order Generation
After saving a signal:

- A scoped `OrderService` instance is created.
- The bot calls:

```
CreateOrdersForActiveUsers(savedSignal)
```

This creates automated trading orders for all active users.

---

## Telegram Messaging Utilities

### Sending Messages / Photos
`PostMessageToGroupAsync` supports:

- Sending text messages  
- Sending images with captions  
- Adding inline keyboard buttons  

### Error Logging
`LoggError` sends errors directly to the configured Telegram error-log group.

---

## Error Handling
Unexpected exceptions trigger `HandleErrorAsync`, which logs errors without interrupting the bot.

---

## Summary
`TelegramBotService` acts as the core engine for:

- Receiving Telegram signals  
- Parsing and validating them  
- Storing them in the database  
- Creating Signals for automated trades  
- Sending notifications and errors back to Telegram  

It ensures only valid and timely signals are processed while supporting multiple signal providers efficiently.

