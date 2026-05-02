# AdminSettingService

**Namespace:** `AutoSignals.Services`  
**Type:** Scoped service

## Overview
`AdminSettingService` is a lightweight key/value feature-flag store backed by the `AdminSettings` database table. It allows runtime toggling of platform features without a deployment.

## Methods

| Method | Description |
|--------|-------------|
| `IsEnabledAsync(key, defaultValue)` | Returns `true` if the setting with the given key has value `"true"`. Returns `defaultValue` if the key does not exist. |
| `SetAsync(key, value)` | Upserts a setting. Creates the record if it doesn't exist, updates it if it does. |

## Usage Example
```csharp
bool chartsEnabled = await _adminSettingService.IsEnabledAsync("KlineChartsEnabled");
await _adminSettingService.SetAsync("KlineChartsEnabled", "false");
```

## Current Known Keys

| Key | Purpose |
|-----|---------|
| `KlineChartsEnabled` | Enables/disables the OHLCV candle chart feature |

## Dependencies
- `AutoSignalsDbContext` — reads/writes `AdminSettings` table
