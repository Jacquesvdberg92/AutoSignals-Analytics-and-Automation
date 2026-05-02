# UserFeedbacksController

**Authorization:** Authenticated users (submit); Admin for management

## Overview
The `UserFeedbacksController` allows users to submit feedback about signals or the platform. Admins can review all submitted feedback.

## Actions

### Create (`POST`)
Submits a new feedback record. Linked to the current user and optionally to a specific signal or provider.

### Index (`GET`) — Admin only
Lists all user feedback records with the ability to filter by category and date.

### Delete (`POST`) — Admin only
Removes a feedback record.

## Dependencies
- `AutoSignalsDbContext` — reads/writes `UserFeedbacks` table
