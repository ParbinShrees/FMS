# Recurring bookings and waitlist

Open **Recurring bookings** in the admin menu, select a venue and create a series. Choose the customer, court, local weekday/time, dates, duration and price mode. Preview every date before saving; select **create free dates only** to retain a plan with conflicts. No conflicting date is forced in.

The detail page shows the occurrence calendar, unresolved conflicts, individual skip/move/cancel/payment actions and future court/time changes. Pause retains existing games; resume fills the horizon. End/archive preserves history. A conflicting future change rolls back completely. Prepaid blocks show the amount allocated to each game; refunds are negative payment records.

**Waitlist** shows the schedule badges, FIFO entries, offers and approval flags. Staff can join for customers and confirm offered games with a recorded payment. Customers use **Notify me if this slot opens**, sign in with OTP, and find offers in **My waitlist**. Offer links show the expiry countdown and use the existing payment flow. A fitting head needing approval or at the hold limit must be resolved before later entries can proceed.

Venue settings control the generation horizon, offer minutes, no-show approval threshold and weekly reminder summary. Daily generation and minute-level recovery run in the API background service. Investigate recurring conflict alerts; they indicate games that could not safely be generated or repriced.

Apply the RecurringAndWaitlist EF migration through the existing backup/migration deployment procedure. Do not alter or disable bookings_no_overlap. For separate development origins configure PublicBooking:BaseUrl (web HTTPS origin) and Realtime:BaseUrl (API HTTPS origin).

Before release, review Scheduling.ne.resx and the new notification templates with a native Nepali speaker. Manually exercise recurring preview/changes, multi-slot offers and expiry on two phones, and verify the summary/offer SMS wording in an authorized provider sandbox. No real provider acceptance or deployment is claimed here.

See [DECISIONS.md](DECISIONS.md) for pricing, cutover, allocation and fairness assumptions.
