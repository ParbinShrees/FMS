# Running the 2–3 venue pilot

Apply the new PilotTools migration with the existing separate migration role and backup process before starting the updated API. Development applies migrations automatically. No production deployment is part of this change.

1. Create the real organization and license using the existing SuperAdmin platform screen. Provision its Owner through the established account process. Do not use a DEMO account for real business.
2. The Owner's login opens `/admin/onboarding`. Save/resume each step: profile/location, courts, weekly hours, optional price overrides, policies, optional eSewa, staff invitations. On finish, copy private staff invitation links once and share directly. Open each link within seven days. Credentials and invitation tokens must not go into feedback screenshots.
3. In `/admin/import`, download a customer template in English or Nepali, fill name/phone, upload and Preview. Fix skipped rows, then import. Maximum 48 KiB / 200 rows per file. Formula cells are rejected; duplicate phone numbers are not overwritten.
4. Import future bookings using court UUID or an unambiguous exact name; ISO times need Z or +05:45. Example time: `2026-10-16T19:00:00+05:45`. Amount paid is a cash receipt. Preview is not a reservation. Conflicts stay rejected. Keep the results visible while correcting rejected rows into a new file.
5. Train front-desk staff using `/admin/help` and the EN/NE language links. Complete a walk-in, payment, cancellation, closure and daily-report exercise before using real customer bookings.
6. Every admin layout has Feedback. Supply category, explanation and optional RGB/RGBA PNG within 192 KiB/2 megapixels. Only the submitting organization and SuperAdmin can access it. SuperAdmin reviews `/admin/feedback` and marks NEW → SEEN → DONE.
7. SuperAdmin opens `/admin/pilot` daily. A pending watchdog is not evidence of zero overlaps. Investigate any critical overlap immediately: pause new bookings, preserve logs/backups, inspect the constraint and the offending rows, and resolve with the owner. Never disable the production constraint to fit a paper booking.
8. Open each venue's weekly summary from the dashboard and screenshot the summary for an in-person owner review. Collect feedback on task confusion, failed payments, SMS, no-shows and cancellations. Keep a separate dated record of the two-week pilot outcome.

For demonstrations, choose Create DEMO on the pilot dashboard. It creates fictional sample data and returns a private Owner login once. Use a private browser window so the real SuperAdmin session stays separate. DEMO SMS and external payments are blocked on the server. Reset restores fictional bookings/customers and sample settings; it never accepts a real organization ID. Demo data is not a source of real receipts.

## Routes and access

| Route | Access |
|---|---|
| `/admin/onboarding` | Owner; server authorizes each save/finalization |
| `/admin/import` | Owner / Manager; venue-scoped courts remain filtered |
| `/admin/help` | Staff guide; contains no private records |
| `/admin/feedback` | Own organization staff / SuperAdmin across organizations |
| `/admin/pilot`, `/admin/pilot/weekly/{venueId}` | SuperAdmin data only |
| `/staff-invite#token` | Public single-use invitation redemption, rate limited |

Feedback/screenshots and onboarding drafts use tenant-filtered tables. The API denies unauthorized data requests even if somebody manually opens a UI URL. All money uses decimal and all persisted times use UTC. Reports display Asia/Kathmandu dates with BS alongside AD.

## Verification

```powershell
dotnet build
dotnet test
```

The suite includes real PostgreSQL import/overlap, demo provider isolation/reset, artificial-overlap watchdog, tenant screenshots, onboarding resume/invitation, HTTP role matrix and all prior regression tests. Human training, screenshot review on real devices and the actual pilot duration remain operational tasks.
