# GDPR Implementation - Personal Data Download & Deletion

## Overview
This document describes the GDPR-compliant data download and deletion functionality implemented in AutoSignals.

## Features

### 1. Download Personal Data
**Location:** `Areas/Identity/Pages/Account/Manage/DownloadPersonalData.cshtml.cs`

Users can download a complete export of all their personal data in JSON format. The export includes:

#### Data Categories Exported:
1. **Identity Data**
   - Basic user account information
   - Email, username, phone number
   - External login providers
   - Two-factor authentication keys

2. **User Profile**
   - Nickname, social media links
   - Subscription tier and status
   - Trial and subscription dates
   - Birth date and notes
   - Starting balance

3. **User Roles**
   - All assigned roles (Admin, User, etc.)

4. **Exchange Connections**
   - Exchange IDs and labels
   - Connection status (active/inactive)
   - Last test results
   - API keys (masked for security - only last 4 characters visible)

5. **Provider Settings**
   - Automation preferences per signal provider
   - Leverage settings
   - Stop-loss configurations
   - Trade size limits

6. **Notification Settings**
   - Email and Telegram notification preferences
   - Signal alert configurations

7. **Trading Data**
   - All positions (open and closed)
   - Complete order history
   - Execution details and timestamps

8. **Portfolio Data**
   - All portfolios created by the user
   - Current and historical holdings
   - Performance metrics

9. **Activity Logs**
   - User visit history
   - IP addresses and user agents
   - Page paths accessed
   - Timestamps

10. **Subscription Events**
    - Payment history
    - Subscription changes
    - Provider information (Stripe, Google Play, etc.)
    - Event details and timestamps

11. **Export Metadata**
    - Export timestamp
    - User ID
    - Export version number

### 2. Delete Personal Data
**Location:** `Areas/Identity/Pages/Account/Manage/DeletePersonalData.cshtml.cs`

Users can permanently delete their account and all associated data. The deletion process:

#### Data Deletion Order (respects foreign key constraints):
1. Portfolio Holdings (child records first)
2. Portfolios
3. Orders
4. Positions
5. Provider Settings
6. Notification Settings
7. Exchange Connections
8. User Visits
9. Subscription Events
10. User Profile Data
11. Identity User Account (includes roles, tokens, claims)

#### Security Features:
- Password confirmation required
- Warning messages displayed before deletion
- Automatic sign-out after deletion
- Comprehensive logging of deletion events
- Irreversible action (cannot be undone)

### 3. User Interface

#### Personal Data Management Page
**Location:** `Areas/Identity/Pages/Account/Manage/PersonalData.cshtml`

Features:
- Clear explanation of GDPR rights
- Detailed list of data categories available for download
- Prominent download button
- Separate section for account deletion with warnings
- Professional, user-friendly design
- Consistent with site branding

#### Delete Confirmation Page
**Location:** `Areas/Identity/Pages/Account/Manage/DeletePersonalData.cshtml`

Features:
- Multiple warning messages
- Detailed list of what will be deleted
- Password confirmation field
- Recommendation to download data first
- Cancel option available
- Professional danger-themed design

## Access

Users can access these features through:
1. Account Settings → Manage Account
2. Navigation menu: "Personal data" link
3. Direct URL: `/Identity/Account/Manage/PersonalData`

The feature is accessible to all authenticated users for their own data.

## Security Considerations

### API Key Protection
- API keys are never exported in plain text
- Only the last 4 characters are shown (masked: `***1234`)
- Full encryption is maintained in the database

### Password Verification
- Users must confirm their password before deletion
- External login users (no password) can delete without password

### Logging
- All download requests are logged
- Account deletions are logged with user ID
- Audit trail maintained for compliance

## Compliance

This implementation satisfies the following GDPR requirements:

### Article 15 - Right of Access
✅ Users can download all their personal data in a structured, commonly used format (JSON)

### Article 17 - Right to Erasure
✅ Users can request complete deletion of their personal data
✅ Deletion is comprehensive and permanent
✅ User is notified of consequences before deletion

### Article 20 - Right to Data Portability
✅ Data is exported in JSON format (machine-readable)
✅ Export includes all personal data categories
✅ No technical barriers to portability

## Technical Notes

### Database Context
Uses `AutoSignalsDbContext` to access all user-related data across multiple tables.

### JSON Export Format
- Pretty-printed for readability
- Null values omitted
- UTF-8 encoding
- Timestamped filename

### Foreign Key Handling
Deletion order is carefully designed to respect database constraints and cascade deletes properly.

### Error Handling
- User not found scenarios handled
- Database errors logged
- Graceful failure messages
- Transaction rollback on errors

## Testing

### Manual Testing Checklist
- [ ] Download data as authenticated user
- [ ] Verify JSON export contains all expected data
- [ ] Verify API keys are masked in export
- [ ] Test account deletion with password
- [ ] Test account deletion without password (external login)
- [ ] Verify user is signed out after deletion
- [ ] Verify all database records are deleted
- [ ] Test cancellation from delete page
- [ ] Verify access control (users can only access their own data)
- [ ] Test error scenarios (wrong password, database errors)

## Future Enhancements

Potential improvements:
1. Add CSV export option alongside JSON
2. Email confirmation before deletion
3. Soft delete with grace period (30 days to recover)
4. Scheduled deletion option
5. Export history tracking
6. Admin notification of deletions
7. GDPR compliance report generation

## Support

For questions or issues related to GDPR compliance, contact the development team or refer to:
- Privacy Policy: `/Home/Privacy`
- Terms & Conditions: `/Pages/terms_conditions`
