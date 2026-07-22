# NinjaSlayer Telemetry Worker

This Worker accepts the RitsuLib `run_history.completed` envelope and anonymous
F2 feedback. Inputs are strictly validated before telemetry is forwarded to
PostHog or feedback is committed to Workers KV.

Raw IP addresses are never stored or forwarded. A server-secret HMAC of the
transient Cloudflare source IP is used for minute limits and Durable Object
daily quotas. Telemetry is limited to 25 MiB per HMAC per day. Feedback is
limited to five submissions and 96 MiB per HMAC per day. A submission-scoped
Durable Object serializes retries so one complete commit wins.

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
`metadata.json`, `screenshot.png`, and the reassembled `logs.zip`.
