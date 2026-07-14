# NinjaSlayer Telemetry Worker

This Worker keeps the existing PostHog `POST` proxy and adds `PUT /feedback`
storage in R2.

## Deployment

```powershell
npm install
npm test
.\node_modules\.bin\wrangler.cmd login
.\node_modules\.bin\wrangler.cmd r2 bucket create ninja-slayer-feedback
.\node_modules\.bin\wrangler.cmd r2 bucket lifecycle set ninja-slayer-feedback --file lifecycle.json --force
.\node_modules\.bin\wrangler.cmd secret list
.\node_modules\.bin\wrangler.cmd deploy
```

`POSTHOG_API_KEY` must already be present before deployment. The lifecycle rule
removes objects under `feedback/` after 180 days.
