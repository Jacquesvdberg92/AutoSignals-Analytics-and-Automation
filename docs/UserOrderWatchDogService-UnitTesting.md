# UserOrderWatchDogService unit testing checklist

## Scope

Focus tests on the adapter-based execution flow and the take-profit percentage behavior.

## Recommended test areas

### 1. Adapter resolution
- Resolve the correct adapter from `order.ExchangeId` for entry orders.
- Resolve the correct adapter from `order.ExchangeId` for take-profit orders.
- Resolve the correct adapter from `order.ExchangeId` for stoploss orders.
- Return a controlled failure when `ExchangeId` is unsupported.

### 2. Credential handling
- Decrypt API credentials before calling the adapter.
- Throw a clear error when `ApiKey` or `ApiSecret` is missing.
- Allow passphrase/password to remain optional for exchanges that do not require it.

### 3. Entry order execution
- When the adapter returns success, verify the order stores:
  - `ExternalOrderId`
  - `ClientOrderId`
  - `ExchangeOrderStatus`
  - `ExchangeResponseJson`
  - `LastSyncTime`
- When the adapter returns error code `45110` or `40762`, verify the order is cancelled through `CloseOrderDueToMinSizeAsync`.
- When the adapter returns a generic failure, verify no position is created or updated.

### 4. Take-profit percentage behavior
- Given an open position size of `10` and TP order size `25`, verify the actual closed size is `2.5`.
- Given an open position size of `10` and TP order size `25`, verify the remaining position size becomes `7.5`.
- Given an open position size of `10` and TP order size `100`, verify the remaining size becomes `0` and the position is marked `CLOSED`.
- Given an open position size of `10` and TP order size `150`, verify the remaining size is clamped to `0` and the position is marked `CLOSED`.
- Verify TP logic always uses the linked open position from `PositionId`, not an absolute quantity from `order.Size`.

### 5. Stoploss execution
- Verify stoploss orders use the resolved adapter.
- Verify test orders skip exchange submission.
- Verify stoploss execution still closes the linked orders and position locally.

### 6. Price fetching
- Verify `FetchLatestPricesAsync` can fetch through any registered adapter.
- Verify users without `ExchangeId` are skipped.
- Verify database fallback is used when no adapter returns a price.

### 7. Error handling
- Verify missing `UserData` fails fast for non-test exchange execution.
- Verify adapter exceptions are logged.
- Verify failed price fetches do not stop processing of other symbols.

## Suggested test doubles
- Mock `ExchangeOrderAdapterFactory` through a scoped service provider.
- Mock `IExchangeOrderAdapter` per exchange.
- Use an in-memory `AutoSignalsDbContext` for orders, positions, and prices.
- Stub `AesEncryptionService` so credential decryption is deterministic.

## High-value test cases
1. Entry order on `Binance` resolves `BinanceOrderAdapter` and persists exchange metadata.
2. Take-profit order on `Bitget` resolves `BitgetOrderAdapter` and closes a percentage of the linked position.
3. Stoploss order on `Okx` resolves `OkxOrderAdapter` and closes the position.
4. Unsupported exchange causes a controlled failure and logs an error.
5. Price fetch succeeds from the first available registered adapter.
6. Price fetch falls back to `GeneralAssetPrices` when all adapter calls fail.
