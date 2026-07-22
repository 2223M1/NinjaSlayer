# Dependency Security Notes

## Wrangler development chain

As of 2026-07-22, `npm audit` reports three high-severity development-tool findings on the path `wrangler 4.113.0 -> miniflare -> sharp <0.35.0`, associated with `GHSA-f88m-g3jw-g9cj`. The affected Sharp/libvips code is used by the local Wrangler/Miniflare toolchain and is not a production dependency of the deployed Worker.

The audit's automatic remediation proposes an incompatible Wrangler downgrade to `4.15.2`. The repository therefore keeps Wrangler pinned at `4.113.0`, runs `npm audit --omit=dev` as the production-dependency gate, and relies on Dependabot to surface a supported Wrangler release whose dependency graph includes Sharp `0.35.0` or newer.

Do not run `npm audit fix --force` for this exception. Re-evaluate and remove this note when an upstream supported dependency chain resolves the advisory.
