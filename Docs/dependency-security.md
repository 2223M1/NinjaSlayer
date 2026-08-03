# Dependency Security Notes

## Wrangler development chain

As of 2026-08-03, the repository pins Wrangler to `4.118.0`. Its dependency graph includes Sharp `0.35.2`, and both `npm audit` and `npm audit --omit=dev` report no known vulnerabilities.

Keep Wrangler pinned to an exact version so Worker builds remain reproducible. Dependabot may propose updates, but each update must retain a clean audit and pass the Worker tests plus `wrangler deploy --dry-run` before it is merged.

Do not run `npm audit fix --force`; update the exact Wrangler version deliberately and review the resulting lockfile instead.
