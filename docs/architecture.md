# Architecture and module boundaries

DocuMind is a modular monolith: one deployable (`DocuMind.Api`) that runs as API, worker or both (`Jobs:Mode`). Each folder under `src/Backend/DocuMind.Api` is a module with a narrow public surface; modules talk through interfaces, not each other's internals.

| Module | Responsibility | Public surface |
|--------|----------------|----------------|
| `Tenancy` | Tenant resolution, membership, roles, token revocation | `ITenantProvider`, `TenantClaims`, policies |
| `Data` / `Entities` / `Migrations` | EF Core model, tenant query filters, optional RLS | `ApplicationDbContext` |
| `Documents` / `Processing` / `Storage` | Upload, PDF text extraction, chunking, embeddings, file storage | `IDocumentProcessingQueue`, `IFileStorage` |
| `Jobs` | Durable Postgres queue, retries, dead-letter, worker host | `IJobQueue`, `IJobHandler` |
| `Chat` | Hybrid retrieval, answers with citations, persisted conversations | `ChatService`, `RetrievalService`, `IChatModel` |
| `Audits` | Compliance checklists and runs | `IAuditQueue` |
| `ApiKeys` | Per-tenant encrypted AI provider keys | `ApiKeyStore` |
| `Activity` | Append-only audit trail of security-relevant actions | `IActivityLog` |
| `Resilience` | Rate limits, quotas, AI concurrency gate, circuit breakers | options + decorators |
| `Observability` | Serilog, OpenTelemetry, health checks, correlation ids | `AddDocuMindObservability` |

Rules of thumb: controllers stay thin and talk to module services; background work reaches the database only through `IJobHandler`s; every tenant-owned table has a `TenantId` and an EF query filter; cross-cutting concerns (resilience, observability) are decorators/middleware, never inlined in feature code.

## Chat history

`POST /api/chat` persists each exchange in `ChatConversations`/`ChatMessages` and emits a `conversation` SSE event first with the conversation id. Clients send `conversationId` on follow-ups; the server then loads the last 20 stored messages as model context (the client-sent history is only used for the first turn). Retrieval for short follow-up questions is run on the previous user question plus the new one (`RetrievalQuery`). Conversations are private to their author; `GET/DELETE /api/conversations` list, read and delete them.

## Activity log

Uploads, deletes, reprocessing, invitations, role changes, member removal, audit runs and API key changes are written to `ActivityLog` with user, IP and correlation id. Admins read it via `GET /api/activity`. Writes never fail the request.

## Optional row-level security

EF query filters are the primary isolation. For defence in depth, apply `src/Backend/DocuMind.Api/Sql/row-level-security.sql` with a privileged role and set `RowLevelSecurity__Enabled=true`. Each connection then sets `app.tenant_id`, and Postgres policies hide other tenants' rows even if a query omitted its filter. Connections without a tenant (workers, sign-in) are allowed through. Run the app as a non-owner role, otherwise RLS is bypassed. This path is off by default and has not been exercised against a live database here.
