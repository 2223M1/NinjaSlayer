# Telemetry And Feedback Privacy

NinjaSlayer can send two kinds of data after the player enables or confirms the corresponding in-game feature.

## Balance Telemetry

- Only completed, non-abandoned NinjaSlayer run-history events are accepted.
- Accepted envelopes are limited to the RitsuLib `run_history.completed` schema for applicant `NinjaSlayer`.
- The Worker does not add, forward, log, or persist the player's real IP address.
- A server-secret HMAC of the transient Cloudflare source IP is used only as a one-minute rate-limit key.
- Telemetry is forwarded to PostHog for aggregate balance analysis.

## F2 Feedback

- The confirmation screen identifies the mod author as the recipient before upload.
- A submission contains the entered category and description, a PNG screenshot, a ZIP log bundle, and game/mod/run context.
- PNG files are limited to 5 MiB, ZIP files to 16 MiB, and the complete request to 24 MiB.
- Feedback objects expire after 180 days. Server receive time determines the storage path and retention window.
- A UUID index makes retries idempotent. Attachments are written first and metadata is written last as the commit marker; failed partial uploads are deleted.

The endpoints are anonymous but enforce server-side HMAC rate limits. No raw source IP or `X-Forwarded-For` value is sent to PostHog or stored in Workers KV.
