# BS date reference data

The 2075?2090 month-length table is pinned from nepali-datetime (PyPI), converted to a small embedded CSV with an AD anchor of 2018-04-14 = 2075-01-01. The tests include 48 reference dates (three per BS year), AD-to-BS, and BS-to-AD. NepDate remains a fallback outside the specified range.

The comparison exposed a one-day disagreement in the prior NepDate dataset beginning in later future years (including 2087-12-01). The specified range now uses one consistent pinned table instead of mixing datasets. Future-year calendars can change and must be verified against Nepal Panchanga Nirnayak Samiti publications before those years are offered for booking. A passing conversion test does not certify unpublished future calendars.

Reference sources: https://pypi.org/project/nepali-datetime/ (MIT); government 2083 holidays confirm 2083-01-01 = 2026-04-14: https://giwmscdntwo.gov.np/media/pdf_upload/Holiday%20for%202083_sj3rtjr.pdf .
