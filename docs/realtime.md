# Real-time availability client behavior

The hub is `GET /hubs/availability`. Public clients can connect anonymously. They may join only active public venues. A date is an ISO `yyyy-MM-dd` calendar date in the venue's configured timezone.

## Initial load

1. Create the SignalR connection and register handlers for `SlotHeld`, `SlotReleased`, `SlotBooked`, `SlotBlocked`, and `SlotCancelled` before starting it.
2. Start the connection and call `JoinVenueDate(venueSlug, date)`. The server resolves the venue slug and joins `venue:{venueId}:date:{yyyy-MM-dd}`.
3. Fetch `GET /api/v1/public/venues/{slug}/availability?date=YYYY-MM-DD` and render the returned grid.
4. Fetch `GET /api/v1/public/venues/{slug}/availability/version?date=YYYY-MM-DD`. Keep the returned `version` with the rendered grid. If the version changed while the grid was loading, fetch the grid and version again.
5. Apply incoming events to the matching court and time range. Event times are UTC; convert them to venue-local display time. `SlotHeld` is unavailable until its `holdExpiresAtUtc`; `SlotBooked` and `SlotBlocked` are unavailable; `SlotReleased` makes the range available; `SlotCancelled` indicates a cancellation and should be reconciled with the availability endpoint.

The public payload contains only `venueId`, `courtId`, `startUtc`, `endUtc`, `status`, and `holdExpiresAtUtc`. It never contains customer identity, booking codes, or payment details. Events describe the full time range even when it spans multiple local dates; the server publishes it to each affected date group.

## Reconnect and missed events

SignalR does not retain missed messages. On every successful reconnect:

1. Rejoin each venue/date group with `JoinVenueDate` (group membership is not restored by the server).
2. Fetch the availability grid again, then fetch its version stamp.
3. Replace the local grid from the response before applying later events.

Every 60 seconds, fetch the version endpoint. If the version differs from the last seen stamp, refetch the availability grid and update the stamp. This also catches hold expirations, which can change availability without a client request. The database remains authoritative; a `409 SLOT_UNAVAILABLE` means the attempted booking lost the race even if an event has not arrived yet.

## Staff group

An authenticated staff client may additionally call `JoinStaffVenueDate(venueSlug, date)`. The hub requires a valid JWT, an Owner, Manager, or Cashier role claim, a current matching membership, and the venue's matching organization and venue scope. Staff groups use `venue:{venueId}:date:{yyyy-MM-dd}:staff`. Anonymous clients and members of another organization are rejected. Staff receive the same privacy-safe availability payload plus `StaffBookingChanged`, which contains only venue/court/booking IDs, times, status, and hold expiry. Customer identity, booking codes, and payment data are never sent.

Each connection may join at most 10 groups, counting public and staff groups together. Join calls are limited to 30 per minute per connection. A configured `Redis:ConnectionString` enables the SignalR Redis backplane; when omitted, the API uses the in-process single-instance transport.

## Manual probe

Run `dotnet run --project tools/Futsal.RealtimeProbe -- http://localhost:5139 <venue-slug> YYYY-MM-DD`. The probe joins the public group, prints the five event types, refetches after reconnect, and polls the version endpoint every 60 seconds.
