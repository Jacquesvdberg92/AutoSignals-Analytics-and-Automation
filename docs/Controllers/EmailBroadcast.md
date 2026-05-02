# EmailBroadcastController

**Authorization:** Admin role only

## Overview
The `EmailBroadcastController` allows admins to compose and send broadcast emails to all registered users (or a filtered subset). It uses the `EmailSender` service to deliver messages.

## Actions

### Index (`GET`)
Displays the broadcast composer form. Admins can enter a subject and HTML body for the email.

### Send (`POST`)
Processes the broadcast form. Fetches all eligible user email addresses from the database and queues them for delivery via `EmailSender`. Returns a summary of how many messages were sent.

## Flow
```
Admin opens broadcast page
  → Fills in subject + HTML body
  → POST to Send
  → All active user emails fetched from database
  → EmailSender sends each message
  → Confirmation shown with send count
```

## Dependencies
- `AutoSignalsDbContext` — fetches user email list
- `EmailSender` / `IEmailSender` — delivers emails
- `UserManager<IdentityUser>` — resolves user records
