# Deployment

## Images

| Image | Dockerfile | Notes |
|-------|-----------|-------|
| API / worker / migrator | `src/Backend/DocuMind.Api/Dockerfile` | One image. `Jobs__Mode=Api`, `Worker` or `All` selects the role; the argument `--migrate` applies EF migrations and exits. |
| Frontend | `src/frontend/Dockerfile` | Next.js standalone output. |

CI (`.github/workflows/ci.yml`) builds both on every PR and publishes to `ghcr.io/<owner>/<repo>/{api,frontend}` on pushes to `main`.

## Local full stack

```
cp .env.example .env   # set POSTGRES_PASSWORD and SESSION_SECRET
docker compose --profile app up --build
```

Order: `db` and `redis` healthy, then `migrate` (one-off), then `api`, `worker`, `frontend`. Scale workers with `--scale worker=3`.

## Migrations as a separate job

Never let several replicas migrate at start-up. Run `DocuMind.Api --migrate` as a one-off job (compose `migrate` service, Container Apps job `<prefix>-migrate`, Kubernetes Job) before rolling out the new version. Migrations are written so that the previous version keeps working during the rollout where possible (expand, then contract in a later release). `AddRetrievalIndexes` rebuilds the HNSW index, which locks writes on `DocumentChunks` and can be slow on large tables; schedule it off-peak.

## Azure skeleton

`infra/bicep/main.bicep` provisions Postgres Flexible Server (pgvector allow-listed), Redis, Blob storage, Container Apps for API, worker and frontend, and the migration job. It has not been deployed from this repository's CI; review SKUs, add private networking, Key Vault references and managed identity before production use.

```
az deployment group create -g <rg> -f infra/bicep/main.bicep \
  -p apiImage=<image> frontendImage=<image> postgresAdminPassword=<secret> sessionSecret=<secret>
az containerapp job start -g <rg> -n documind-staging-migrate
```

## Staging notes

- Keep staging structurally identical to production (same images, `Jobs__Mode` split, Redis, blob storage) but with the smallest SKUs and a separate database, storage account and AI provider keys.
- Deploy flow: merge to `main` -> CI publishes `sha-<commit>` images -> deploy to staging -> run migration job -> smoke test `/health/ready` and a sample upload/chat -> promote the same image tags to production.
- Set `ForwardedHeaders__KnownNetworks` to the frontend's network so the real client IP is trusted for rate limiting.
- Point `OpenTelemetry__OtlpEndpoint` at your collector; set `Serilog` minimum levels per environment.
- Secrets (database, Redis, blob, session secret, AI keys) come from the platform secret store, never from the repository.
