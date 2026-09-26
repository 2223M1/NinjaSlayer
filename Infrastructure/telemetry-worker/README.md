# NinjaSlayer Telemetry Worker

For the GitHub Pages public observatory and loopback-only private dashboard, see
[观测室部署、启动与统计口径](dashboard/README.md). Run `npm run dashboard` for private administration.

This Worker accepts the RitsuLib `run_history.completed` envelope (`run_history` or `balance_runs` requests) and anonymous
F2 feedback. Inputs are strictly validated before telemetry is forwarded to
PostHog or feedback is committed to private R2 storage. Separately authorized `battle_report.completed` envelopes are validated, anonymized and compressed into R2 for 90 days; they never enter PostHog. Small indexes and feedback metadata remain in Workers KV. The public report index/detail routes expose only the validated projection. Feedback and replays share the storage budget in `src/free-storage.js`.

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

The game and observatory use `https://telemetry.feixingwawa.cn`. This custom
domain points to the existing Worker; the `workers.dev` address stays enabled
for older clients. `api.feixingwawa.cn` belongs to a separate service and must
not be changed. Keep the custom domain in `wrangler.jsonc` so deployments retain it.

`GET /batch/` returns HTTP 405 with `Only POST is accepted`; this checks routing
without submitting data. Check reachability on the affected network without a
proxy before releasing an endpoint change. The native upload contracts below
verify request handling separately from that network check.

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

`RECORDS_BUCKET` binds the private Standard-class bucket `ninja-slayer-records`.
Do not enable an R2 public domain or `r2.dev` access: public reports must pass
through the Worker, and feedback attachments remain administrator-only.
Configure native lifecycle deletion for `feedback/` after 180 days and `replays/`
after 90 days. Keep the absolute-date migration rules until the old objects expire;
do not replace the whole lifecycle policy with only the two general rules.

## Free storage limits

R2 Standard's account-wide free allowance is 10 GB-month, 1 million Class A and
10 million Class B operations per month. This project's hard reservations stop
before 8,000,000,000 bytes, 2,000 writes/day and 20,000 uncached public reads/day.
Daily ceilings remain well below monthly operation allowances even in a 31-day month.
The existing KV limits stay at 800 MiB and 800 writes/day. Cache hits do not read R2.
Uploads and uncached reads receive a retryable 429 when their budget is exhausted;
existing data is not replaced and no successful receipt is fabricated.

At 7 GB of reserved capacity, the scheduled cleanup removes the oldest original
uploads first; 8 GB remains the hard upload ceiling. The 180/90-day periods are
maximum retention, so capacity pressure may remove older raw records sooner.
Feedback deletion also removes its metadata/index and writes a tombstone; replay
deletion removes its public index and closes its receipt to later updates.
PostHog aggregates remain independent. Each alarm handles at most eight objects,
then resumes after a minute when more work remains, within free Worker request limits.

The existing shared Durable Object (`kv-free-budget-v1`, retained to preserve
receipts) reserves bytes before every upload. Failed uploads and replacements are
conservatively charged. The cleanup alarm confirms object deletion before releasing
capacity; cleanup failures keep it charged. The alarm also enforces a
replay's original deadline after replacements. Native R2 lifecycle rules remove
orphans independently. Administrative reads and deletes use Wrangler; deletion
is free, while expiry HEAD checks and occasional administrative operations fit
within the remaining Class B headroom. Direct bucket writes or other applications
in this Cloudflare account bypass these project limits. Cloudflare budget alerts
and an empty payment account are not hard spending caps.

The KV migration preserves original object keys, bytes, expiry and metadata hashes.
Historical metadata still says `workers-kv` because it is part of its hash-bound
receipt; all attachment reads use R2 after cutover. New logs are one ZIP object,
while the download tools still join the historical chunk list. Private backups
and the detailed migration inventory belong under ignored `build/r2-migration/`.

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

The Pages export job reads `GET /observatory/feedback` with an
`OBSERVATORY_READ_TOKEN` bearer secret shared with Actions. Pagination uses the
returned cursor. It reads completed metadata only and does not serve attachments.
Only new feedback carrying the explicit `publishDescription: true` notice flag
is projected into the public artifact; historical feedback remains private.

## Native upload regression

The RitsuLib PostHog adapter sends `POST /batch/`; `/batch` and the existing `/`
entry use the same receiver. Game feedback uses `PUT /feedback` and .NET multipart
with quoted disposition names and filenames. A successful feedback response must
contain `ok: true` and the matching submission `id`.

`test/uploads.test.js` runs the production Worker under Miniflare with local KV, R2 and
Durable Objects. Its fixture is captured from the actual product DLL and RitsuLib
HTTP adapter by `VerifyUploadTransport` in the product contracts. Set
`NINJASLAYER_CONTRACT_ONLY_UPLOADS=1` and `NINJASLAYER_UPLOAD_FIXTURE_DIR` to export
fresh requests for either host; run the Node test with `NINJASLAYER_UPLOAD_FIXTURE`
pointing to that directory's `requests.json`. External PostHog delivery is mocked;
feedback, attachments and retry receipts are verified against local storage.
