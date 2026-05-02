# AesEncryptionService

**Namespace:** `AutoSignals.Services`  
**Type:** Singleton service

## Overview
`AesEncryptionService` provides AES-256 symmetric encryption and decryption for sensitive data stored in the database — primarily exchange API keys and secrets entered by users.

## Methods

| Method | Description |
|--------|-------------|
| `Encrypt(plainText)` | Encrypts a string using AES-256-CBC. Returns a Base64-encoded ciphertext with the IV prepended. |
| `Decrypt(cipherText)` | Decrypts a Base64-encoded AES ciphertext back to the original string. |

## Security Notes
- The encryption key is loaded from application configuration (`AesKey` in `appsettings` or environment variables). **Never commit the key to source control.**
- The IV is randomly generated per encryption and prepended to the output — each encryption of the same value produces a different ciphertext.
- Only the decrypted key is ever sent to the exchange API; raw keys are never stored.

## Flow
```
User enters API Key + Secret in Settings
  → AesEncryptionService.Encrypt(apiKey)
  → AesEncryptionService.Encrypt(apiSecret)
  → Encrypted values stored in UserExchangeConnection table
  → When order is placed:
      → AesEncryptionService.Decrypt(storedKey) → passed to exchange adapter
```
