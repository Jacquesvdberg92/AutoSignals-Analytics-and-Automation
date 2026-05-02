# ExchangesController

**Authorization:** Admin role only

## Overview
The `ExchangesController` manages the supported exchange configurations stored in the database. Each exchange record controls whether it is enabled, what label it shows, and what features it supports.

## Actions

### Index (`GET`)
Lists all exchanges in the system with their current enabled/disabled status.

### Edit (`GET/POST`)
Allows admins to update exchange settings (name, enabled state, supported features).

### Create / Delete
Standard CRUD for adding or removing exchange records. Rarely used after initial seeding.

## Dependencies
- `AutoSignalsDbContext` — reads/writes `Exchanges` table
