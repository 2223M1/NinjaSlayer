# Telemetry And Feedback Privacy

NinjaSlayer can send two kinds of data after the player enables or confirms the corresponding in-game feature.

## Balance Telemetry

- Only completed, non-abandoned NinjaSlayer run-history events are accepted.
- Accepted envelopes are limited to the RitsuLib `run_history.completed` schema for applicant `NinjaSlayer`.
- The Worker does not add, forward, log, or persist the player's real IP address.
- A server-secret HMAC of the transient Cloudflare source IP is used only as a one-minute rate-limit key.
- The same non-reversible HMAC is limited to 25 MiB of accepted telemetry per UTC day by a Durable Object.
- Telemetry is forwarded to PostHog for aggregate balance analysis.

## F2 Feedback

- The confirmation screen identifies the mod author as the recipient before upload.
- A submission contains the entered category and description, a PNG screenshot, a ZIP log bundle, and game/mod/run context.
- PNG files are limited to 5 MiB, ZIP files to 16 MiB, and the complete request to 24 MiB.
- Feedback objects expire after 180 days. Server receive time determines the storage path and retention window.
- Each HMAC is limited to five accepted submissions and 96 MiB per UTC day.
- A submission-scoped Durable Object owns a renewable two-minute writing lease. Attachments and metadata use an attempt-specific path, and the final UUID index binds the winning attempt to the SHA-256 of its metadata.
- A valid lease returns a processing response; an expired lease can be taken over. Losing attempts can delete only their own paths, while administrative deletion leaves a retained UUID tombstone so retries cannot recreate removed feedback.

The endpoints are anonymous but enforce server-side HMAC rate limits. No raw source IP or `X-Forwarded-For` value is sent to PostHog or stored in Workers KV.
