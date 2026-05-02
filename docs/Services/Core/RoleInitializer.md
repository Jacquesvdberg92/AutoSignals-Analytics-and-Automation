# RoleInitializer

**Namespace:** `AutoSignals.Services`  
**Type:** Hosted service (`IHostedService`)

## Overview
`RoleInitializer` runs once at application startup and ensures all required ASP.NET Identity roles exist in the database. This prevents missing-role errors when the app is deployed to a fresh environment.

## Flow
```
Application starts
  → RoleInitializer.StartAsync()
  → For each required role ["Admin", "VIP", "Pro", "Tester"]:
      → RoleManager.RoleExistsAsync(role)
      → If not → RoleManager.CreateAsync(new IdentityRole(role))
```

## Roles Created

| Role | Purpose |
|------|---------|
| `Admin` | Full platform access including all admin pages |
| `VIP` | Premium subscriber tier — full feature access |
| `Pro` | Mid-tier subscriber |
| `Tester` | Internal testers — VIP-equivalent access |

## Dependencies
- `RoleManager<IdentityRole>` — ASP.NET Identity role management
