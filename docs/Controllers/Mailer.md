# MailerController

**Authorization:** Admin role only

## Overview
The `MailerController` provides a direct email testing interface. It allows admins to send test emails to verify SMTP/email-provider configuration is working correctly.

## Actions

### Index (`GET`)
Displays a simple email test form.

### Send (`POST`)
Sends a test email to the specified address using `EmailSender`. Confirms delivery or reports errors.

## Dependencies
- `IEmailSender` / `EmailSender` — delivers test emails
