# UsersDataController

**Authorization:** Admin role only

## Overview
The `UsersDataController` provides the admin view of all registered users. It shows user accounts, their subscription tiers, join date, last visit, and activity indicators.

## Actions

### Index (`GET`)
Paginated list of all users with columns: email, role, tier, trial status, last visit, join date. Supports search by email.

### Details (`GET`)
Full user profile view showing: subscription history, connected exchanges (masked), order history, feedback submissions, and visit log.

### Delete (`POST`)
Deletes a user account and all associated data. Requires confirmation.

## Dependencies
- `AutoSignalsDbContext` — all user-related tables
- `UserManager<IdentityUser>` — identity records
- `ISubscriptionService` — current tier lookup
