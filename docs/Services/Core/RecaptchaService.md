# RecaptchaService

**Namespace:** `AutoSignals.Services`  
**Type:** Scoped service

## Overview
`RecaptchaService` verifies Google reCAPTCHA v3 tokens submitted with forms. It calls the Google reCAPTCHA verification API and returns the full response including the score, action, and success flag.

## Methods

| Method | Description |
|--------|-------------|
| `VerifyAsyncFull(recaptchaResponse)` | Posts the token to `https://www.google.com/recaptcha/api/siteverify`. Returns a `RecaptchaVerifyResponse` with `Success`, `Score`, and `Action`. |

## Response Model: `RecaptchaVerifyResponse`

| Property | Description |
|----------|-------------|
| `Success` | Whether the challenge was passed |
| `Score` | Confidence score (0.0 = bot, 1.0 = human) |
| `Action` | The action name provided at token generation |
| `ErrorCodes` | Any error codes returned by Google |

## Configuration
- `Recaptcha:SecretKey` — server-side secret from Google reCAPTCHA console

## Usage
Forms that use reCAPTCHA v3 include the invisible token in the POST body. The controller passes it to `VerifyAsyncFull`. Requests with `Score < 0.5` or `Success == false` are typically rejected.
