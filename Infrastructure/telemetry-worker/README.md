# NinjaSlayer Telemetry Worker

This Worker accepts the RitsuLib `run_history.completed` envelope and anonymous
F2 feedback. Inputs are strictly validated before telemetry is forwarded to
PostHog or feedback is committed to Workers KV.

Raw IP addresses are never stored or forwarded. A server-secret HMAC of the
transient Cloudflare source IP is used for minute limits and Durable Object
daily quotas. Telemetry is limited to 25 MiB per HMAC per day. Feedback is
limited to five submissions and 96 MiB per HMAC per day. A submission-scoped
Durable Object owns a renewable two-minute write lease so one complete attempt
wins even when requests overlap or an isolate is restarted.

Feedback attachments and metadata live below an attempt-specific prefix. The
stable `feedback-index/<submissionId>` key is first a writing marker and becomes
a completion marker only after every object is present. The completion marker
binds the winning attempt to the SHA-256 of its metadata. Valid retries return
retryable `503` while the lease is active, take over an expired lease, or repair a missing
index marker from durable completion state. Administrative deletion writes a
retained tombstone before removing data so a late retry cannot recreate it.

## Deployment

```powershell
npm install
npm test
.\node_modules\.bin\wrangler.cmd login
.\node_modules\.bin\wrangler.cmd kv namespace create NinjaSlayerFeedback --binding FEEDBACK_KV --update-config
.\node_modules\.bin\wrangler.cmd secret list
.\node_modules\.bin\wrangler.cmd deploy
```

`POSTHOG_API_KEY` and `RATE_LIMIT_SALT` must be present before deployment. The
two rate-limit bindings, `ANONYMOUS_QUOTAS`, `FEEDBACK_SUBMISSIONS`, and
`FEEDBACK_KV` are also mandatory; production requests fail closed with `503`
when any security dependency is unavailable.

Run `npx wrangler deploy --dry-run` before a live deployment. The Worker accepts
only snake_case `applicant_id` and `request_id` fields from the RitsuLib 0.4.62
contract; camelCase and unknown envelope fields are rejected.

## Feedback administration

```powershell
npm run feedback -- list
npm run feedback -- download <submission UUID>
npm run feedback -- delete <submission UUID>
```

Downloads are written to `feedback-downloads/<submission UUID>/` with
`metadata.json`, `screenshot.png`, and the reassembled `logs.zip`. The tool only
accepts completed schema-2 index markers whose attempt, metadata path, and
metadata SHA-256 agree; writing, malformed, and legacy weak markers are not
treated as completed submissions.
