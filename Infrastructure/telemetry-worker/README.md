# NinjaSlayer Telemetry Worker

This Worker keeps the existing PostHog `POST` proxy and adds `PUT /feedback`
storage in the free Workers KV allowance. Logs are split into 20 MiB chunks and
all feedback keys expire after 180 days.

## Deployment

```powershell
npm install
npm test
.\node_modules\.bin\wrangler.cmd login
.\node_modules\.bin\wrangler.cmd kv namespace create NinjaSlayerFeedback --binding FEEDBACK_KV --update-config
.\node_modules\.bin\wrangler.cmd secret list
.\node_modules\.bin\wrangler.cmd deploy
```

`POSTHOG_API_KEY` must already be present before deployment.

## Feedback administration

```powershell
npm run feedback -- list
npm run feedback -- download <submission UUID>
npm run feedback -- delete <submission UUID>
```

Downloads are written to `feedback-downloads/<submission UUID>/` with
`metadata.json`, `screenshot.png`, and the reassembled `logs.zip`.
