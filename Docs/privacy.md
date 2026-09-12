# Telemetry And Feedback Privacy

NinjaSlayer provides an in-game data-sharing setting and a separate confirmation for each F2 feedback submission.

## Balance Telemetry

- Only completed, non-abandoned NinjaSlayer run-history events are accepted.
- Accepted envelopes are limited to the RitsuLib `run_history.completed` schema for applicant `NinjaSlayer`.
- The Worker does not add, forward, log, or persist the player's real IP address.
- A server-secret HMAC of the transient Cloudflare source IP is used only as a one-minute rate-limit key.
- The same non-reversible HMAC is limited to 25 MiB of accepted telemetry per UTC day by a Durable Object.
- Telemetry is forwarded to PostHog for aggregate balance analysis.
- The `balance_runs` request covers run history plus per-combat card draws, manual/automatic plays, repeated resolutions and energy/stars actually paid. No damage-to-card attribution is inferred.
- Data sharing defaults on with a first-launch notice before delivery. Accepting enables it immediately; dismissing keeps delivery off for that process and enables it on the next game launch. Explicit rejection or disabling in NinjaSlayer settings persists. Existing RitsuLib rejection and a historical `run_history`-only grant are preserved. No historical runs are backfilled.
- Combat summaries use RitsuLib run saved data and are sent only with a permitted completed-run event. Replaying a saved room replaces its previous measurement; older or unmeasured rooms remain missing.
- The existing native snapshot contains run/player identifiers used to join choices to players. UInt64 identifiers cross JavaScript services as decimal strings to preserve precision. Public artifacts contain aggregate counters, without identifiers, seeds, complete decks or raw runs.
- The public observatory is hosted at https://2223m1.github.io/NinjaSlayer/ and updated by GitHub Actions. Query credentials remain in Actions secrets. The private developer dashboard listens only on loopback and holds its query credentials in the local process; attachments are read on demand there. See `Infrastructure/telemetry-worker/dashboard/README.md` for the statistical definitions and refresh behavior.

## F2 Feedback

- The confirmation screen states that the description will be public, while screenshots and logs remain private to the mod author. Only submissions carrying this new notice's `publishDescription: true` flag are published; previous feedback remains private.
- The public site includes description, category, submission time and game/mod versions. Screenshots, logs and run context are excluded. A successful scheduled sync removes deleted or expired entries from the current site; previously downloaded public copies cannot be recalled.
- A submission contains the entered category and description, a PNG screenshot, a ZIP log bundle, and game/mod/run context.
- PNG files are limited to 5 MiB, ZIP files to 16 MiB, and the complete request to 24 MiB.
- Feedback objects expire after 180 days. Server receive time determines the storage path and retention window.
- Each HMAC is limited to five accepted submissions and 96 MiB per UTC day.
- A submission-scoped Durable Object owns a renewable two-minute writing lease. Attachments and metadata use an attempt-specific path, and the final UUID index binds the winning attempt to the SHA-256 of its metadata.
- A valid lease returns a processing response; an expired lease can be taken over. Losing attempts can delete only their own paths, while administrative deletion leaves a retained UUID tombstone so retries cannot recreate removed feedback.

The endpoints are anonymous but enforce server-side HMAC rate limits. No raw source IP or `X-Forwarded-For` value is sent to PostHog or stored in Workers KV.

启用过自由操控的整局不会上报平衡统计。标记随该局存档保存，中途关闭开关或读档不恢复统计资格。
