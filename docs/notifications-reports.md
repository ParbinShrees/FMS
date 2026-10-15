# Milestone 8: notifications and reports

## Development

1. Start PostgreSQL and run API/Web as described in README. Migrations are applied by the existing API startup process.
2. Use `ASPNETCORE_ENVIRONMENT=Development`. Development configuration sets `Sms:Sender=Console`; there are no SMS credentials to enter for console testing.
3. Create a staff booking with a valid Nepali phone, e.g. `+9779800000000`. The API queues one confirmation after commit; within the next minute the console prints the message with the recipient masked. A payment queues its own receipt. Creating a booking starting within two hours triggers its reminder at the next five-minute reminder job.
4. In `/admin/notifications`, select the venue and Load. Edit either language, inspect the live preview/segment warning, change limits and reminder hours, and view log/usage. A cap of zero blocks all venue SMS and raises the persistent alert. FAILED messages can be manually retried, subject to all send checks.
5. Request an OTP through the public venue page. Its venue/language context selects the venue OTP template. The development console shows the code after the queue worker runs. Never enable console sending in Production.
6. In `/admin/reports`, select venue, report, and AD dates (or enter BS dates and Apply). Shift reports also accept Nepal-local start/end times and an optional cashier user ID for managers. Cashiers are restricted by the API to their own shift. Download CSV/PDF from the result. Choose English/Nepali in the header; UI and export labels follow that culture.

## SMS provider configuration

Use user-secrets or environment variables; never commit an API key:

```powershell
dotnet user-secrets set "Sms:ApiKey" "<provider-issued-key>" --project src/Futsal.Api
dotnet user-secrets set "Sms:ProviderUrl" "https://<provider>/sms" --project src/Futsal.Api
dotnet user-secrets set "Sms:SenderId" "<approved-sender-id>" --project src/Futsal.Api
dotnet user-secrets set "Sms:Sender" "Http" --project src/Futsal.Api
```

Equivalent environment names: `Sms__ApiKey`, `Sms__ProviderUrl`, `Sms__SenderId`, `Sms__Sender`, `Sms__ContractConfirmed`.

The generic adapter lives entirely in `src/Futsal.Infrastructure/Notifications/SmsSenders.cs`, class `HttpSmsSender`. It is deliberately disabled until `Sms:ContractConfirmed=true`. **Before enabling, confirm and adjust its marked TODO contract:**

- HTTPS endpoint and method; bearer token authentication versus the provider's actual auth scheme.
- Payload field names (`to`, `message`, `sender`) and E.164 recipient format.
- Whether `Idempotency-Key` is supported, its retention period, and whether duplicate keys avoid duplicate billing/delivery.
- Whether HTTP 2xx alone means accepted, or a response body/error code must also be checked. Implement that check in this one adapter class.
- Nepal Unicode/concatenation encoding, segment rates, approved sender ID, throttling, and timeout behavior.

The adapter has a 10-second timeout, retries through the durable queue, and never logs provider response bodies. Queue errors are safe stable codes. Delivery is not claimed when the provider contract or credentials are unknown.

Caps charge potential segments before every attempt, including uncertain timeouts. Monthly usage distinguishes these reservations from provider-accepted segments. Standalone login OTPs without a venue are platform messages, not charged to a venue. Persist the API Data Protection key ring so queued messages survive restarts with decryptable payloads.

## API

- `GET /api/v1/reports/{daily|monthly|dues|utilization|customers|shift}?venueId=...&from=yyyy-MM-dd&to=yyyy-MM-dd`
- Add `format=csv` or `format=pdf` for exports; export access is audited. Add `cashierId` and ISO-8601 `shiftStart`/`shiftEnd` for exact shift windows.
- `/api/v1/admin/venues/{id}/notifications/settings` GET/PUT; `templates` GET/PUT; `preview` POST; `logs` GET (page/pageSize); `usage` GET; `{notificationId}/resend` POST.
- Settings/templates/logs/usage and full reports require Owner/Manager membership and venue access. Public OTP requests accept optional `venueId` and `language`.

See DECISIONS.md for accounting/date/utilization definitions and delivery assumptions.

## Verification and licenses

`dotnet build` and `dotnet test` run the complete solution. Docker must be available for the PostgreSQL Testcontainers tests; they use disposable databases and do not touch development data. Tests cover queue deduplication/races, reminders, cancellation/expiry, opt-out, cap enforcement, retries, due throttling, OTP encryption/templates, tenant/role boundaries, signed report totals/refunds, Nepal midnight, CSV escaping, and Nepali PDF/font/resources.

QuestPDF is **not MIT**: review its [license selection guide](https://www.questpdf.com/license/guide.html). Eligible small organizations under USD 1M annual gross revenue can use Community; other organizations may need Professional/Enterprise. Set `Reports:PdfLicense` to the license you qualify for. NepDate is MIT; the bundled Noto Sans Devanagari font uses SIL OFL 1.1 (license alongside font).
