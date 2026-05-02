# EmailSender

**Namespace:** `AutoSignals.Services`  
**Type:** Transient service (implements `IEmailSender`)

## Overview
`EmailSender` is the platform's email delivery service. It implements the ASP.NET Identity `IEmailSender` interface and is used throughout the application for account emails, broadcast campaigns, and notifications.

## Method

| Method | Description |
|--------|-------------|
| `SendEmailAsync(email, subject, htmlMessage)` | Sends an HTML email to the specified address. |

## Configuration
Email delivery settings are pulled from application configuration:

| Setting | Purpose |
|---------|---------|
| `Email:SmtpHost` | SMTP server hostname |
| `Email:SmtpPort` | SMTP port (typically 587) |
| `Email:SmtpUser` | SMTP username / sender address |
| `Email:SmtpPass` | SMTP password |
| `Email:FromName` | Display name for the From field |

## Usage Contexts
- **Account emails** — confirmation, password reset (via ASP.NET Identity)
- **Broadcast** — `EmailBroadcastController` sends to all users
- **Test** — `MailerController` for SMTP verification

## Notes
- Emails are sent synchronously within the calling thread. For high-volume broadcast, consider queuing.
- HTML body is sent as-is — ensure content is properly sanitised before passing.
