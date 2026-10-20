# DocuMind AI

DocuMind AI is a multi-tenant B2B SaaS platform for working with large collections of business documents. Companies upload PDFs such as contracts, compliance records and technical manuals. DocuMind extracts their text, indexes it for semantic (vector) search, and lets people ask questions in plain language. Answers come from Claude and cite the exact document and page they're based on.

> **Status:** early development. Uploading, processing, vector search, question answering with citations, bring-your-own-key settings, and sign-in with companies, roles and invitations work end to end. Compliance audits and production deployment are not built yet (see the [Roadmap](#roadmap)).

---

## Contents

- [What it does](#what-it-does)
- [Technology stack](#technology-stack)
- [Architecture](#architecture)
  - [System overview](#system-overview)
  - [Document ingestion pipeline](#document-ingestion-pipeline)
  - [Question answering (RAG)](#question-answering-rag)
  - [Bring your own key (AI providers)](#bring-your-own-key-ai-providers)
  - [Sign-in, companies and roles](#sign-in-companies-and-roles)
  - [Multi-tenancy](#multi-tenancy)
  - [Data model](#data-model)
  - [Key design decisions](#key-design-decisions)
- [Repository structure](#repository-structure)
- [Getting started](#getting-started)
- [Configuration](#configuration)
- [API reference](#api-reference)
- [Development workflow](#development-workflow)
- [Known limitations](#known-limitations)
- [Roadmap](#roadmap)

---

## What it does

| Feature | Description |
|---|---|
| **Upload** | Drag and drop PDFs up to 50 MB. Files are checked by their content (not just their name) and stored per tenant. |
| **Automatic processing** | A background worker extracts text page by page, splits it into overlapping chunks, and creates a vector embedding for each chunk with Voyage AI. |
| **Live status** | Each document shows *Pending → Processing → Processed* (or *Failed*, with the reason and a Retry button). The list refreshes itself while work is in progress. |
| **Ask your documents** | Ask a question in plain language. The most relevant chunks are found by vector similarity and sent to the chosen AI model (Claude, OpenAI or Gemini), which answers only from them. |
| **Bring your own key** | Each company pastes its own API keys on the **Settings** page. Keys are verified with the provider, encrypted before storage, and never shown again. Usage is billed to the company's own accounts. |
| **Citations** | Every claim in an answer is marked with a citation like **[1]**. Clicking it opens the source PDF at the cited page. |
| **Sign-in and teams** | People sign in with email and password. Signing up creates a company with you as its **Admin**; Admins invite colleagues by link as Admin or Member and manage roles on the **Team** page. |
| **Tenant isolation** | Every document, chunk, key and search is scoped to the signed-in user's company, and the database layer enforces this automatically. |

---

## Technology stack

### Frontend: `src/frontend`

| Technology | Version | Role |
|---|---|---|
| [Next.js](https://nextjs.org) (App Router) | 16.2 | Web framework: server-rendered pages, Server Actions, route handlers |
| [React](https://react.dev) | 19.2 | UI components (`useActionState`, `useTransition`) |
| [TypeScript](https://www.typescriptlang.org) | 5 | Type safety |
| [Tailwind CSS](https://tailwindcss.com) | 4 | Styling, including dark mode |
| [`server-only`](https://www.npmjs.com/package/server-only) | — | Guarantees API-client code never ships to the browser |

### Backend: `src/Backend/DocuMind.Api`

| Technology | Version | Role |
|---|---|---|
| [ASP.NET Core](https://learn.microsoft.com/aspnet/core) | .NET 10 | REST API, server-sent events, background worker |
| [Entity Framework Core](https://learn.microsoft.com/ef/core) + [Npgsql](https://www.npgsql.org/efcore/) | 10 | Database access and migrations |
| [Pgvector.EntityFrameworkCore](https://github.com/pgvector/pgvector-dotnet) | 0.3 | `vector` column type and cosine-distance queries |
| [PdfPig](https://github.com/UglyToad/PdfPig) | 0.1.16 | PDF text extraction |
| [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp) | 12.58 | Claude Messages API (streaming, citations) |
| [Microsoft.Extensions.Http.Resilience](https://learn.microsoft.com/dotnet/core/resilience/http-resilience) | 10.11 | Retries with backoff, timeouts and a circuit breaker for Voyage calls |
| [Scalar](https://github.com/scalar/scalar) + built-in OpenAPI | — | Interactive API reference at `/scalar/v1` (development only) |

### Data and AI services

| Service | Role |
|---|---|
| **PostgreSQL 17 + [pgvector](https://github.com/pgvector/pgvector)** | Relational data, plus vector storage with an HNSW index for fast similarity search. Runs in Docker (`pgvector/pgvector:pg17`). |
| **[Voyage AI](https://www.voyageai.com)** (`voyage-4`, 1024 dimensions) | Turns text chunks and questions into embeddings. Documents and questions use different `input_type` settings (`document` and `query`) for better retrieval. |
| **[Claude](https://www.anthropic.com/claude)** (default `claude-opus-5-5`) | Writes the answers (recommended). Chunks are sent as *search result* blocks with citations enabled, so Claude's answer references them directly. |
| **[OpenAI](https://platform.openai.com) / [Gemini](https://ai.google.dev)** | Alternative answer providers through the official [OpenAI .NET SDK](https://github.com/openai/openai-dotnet). Gemini is reached through Google's OpenAI-compatible endpoint. They cite sources as `[n]` markers requested in the prompt. |

---

## Architecture

### System overview

```mermaid
flowchart LR
    Browser["Browser<br/>(React UI)"]

    subgraph Next["Next.js server :3000"]
        Pages["Server Components<br/>(document list)"]
        Actions["Server Actions<br/>(upload, delete, retry)"]
        Routes["Route handlers<br/>/api/chat · /documents/[id]/file"]
    end

    subgraph Api[".NET API :5288"]
        DocsCtl["DocumentsController"]
        ChatCtl["ChatController (SSE)"]
        Worker["DocumentProcessingWorker<br/>(background)"]
        Queue[["In-memory queue"]]
    end

    Disk[("Local file storage<br/>uploads/{tenant}/")]
    DB[("PostgreSQL + pgvector")]
    Voyage["Voyage AI<br/>embeddings"]
    Claude["Claude API<br/>answers + citations"]

    Browser --> Pages & Actions & Routes
    Pages & Actions & Routes -- "HTTP + Bearer token" --> DocsCtl & ChatCtl
    DocsCtl --> Disk & DB
    DocsCtl -- enqueue --> Queue --> Worker
    Worker --> Disk & DB & Voyage
    ChatCtl --> DB & Voyage & Claude
```

The system has three parts:

1. **Next.js (backend-for-frontend).** The browser only talks to the Next.js server. Next.js renders the document list on the server, handles uploads and deletes through **Server Actions**, and forwards the chat and download streams through **route handlers**. The .NET API's address and the user's access token stay on the server: the token lives in an encrypted, HttpOnly session cookie and is added to each API call by the Next.js server.
2. **The .NET API.** Holds all business logic: validation, storage, the processing pipeline, retrieval and the Claude integration. It is the only component that touches the database, the files and the AI services.
3. **PostgreSQL with pgvector.** One database holds both the relational data (documents, status) and the vectors (chunk embeddings). No separate vector database is needed.

### Document ingestion pipeline

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Next as Next.js (Server Action)
    participant API as DocumentsController
    participant Store as File storage
    participant DB as PostgreSQL
    participant Q as Queue
    participant W as Processing worker
    participant V as Voyage AI

    User->>Next: Drop PDF + optional title
    Next->>API: POST /api/documents (multipart)
    API->>API: Check size ≤ 50 MB and %PDF- signature
    API->>Store: Save uploads/{tenant}/{id}.pdf
    API->>DB: Insert Document (status Pending)
    API->>Q: Enqueue document id
    API-->>Next: 201 Created
    Next-->>User: List refreshes, shows "Pending"

    Q->>W: Next document id
    W->>DB: Status → Processing
    W->>Store: Read PDF
    W->>W: Extract text per page (PdfPig)
    W->>W: Split into ~1,500-char chunks, 200-char overlap
    W->>V: Embed chunks (batches of 128, input_type=document)
    V-->>W: 1024-dimension vectors
    W->>DB: In one transaction: replace chunks, status → Processed
    Note over W,DB: On any error: status → Failed + ProcessingError
```

How chunking works ([`TextChunker`](src/Backend/DocuMind.Api/Processing/TextChunker.cs)):
- **Chunks never cross pages**, so every chunk has an exact page number for citations.
- **Size:** about 1,500 characters (roughly 350–400 tokens), small enough for precise retrieval but large enough to keep a clause together.
- **Break points:** a paragraph break if possible, then a sentence end, then a word boundary.
- **Overlap:** 200 characters, so a sentence split across two chunks still appears whole in one of them.

The pipeline is built to recover from failures:
- **Retries:** Voyage calls are retried on rate limits (429) and server errors with exponential backoff and jitter.
- **Restarts:** the queue lives in memory, so on startup the worker re-queues every document still `Pending` or `Processing`.
- **Reprocessing:** a document can be processed again (`POST /api/documents/{id}/reprocess`). Old chunks are swapped for new ones in a single transaction, so a document is never left half-indexed.

### Question answering (RAG)

DocuMind uses **Retrieval-Augmented Generation (RAG)**: rather than trusting the model's general knowledge, it first *retrieves* the relevant passages from your documents and asks Claude to answer *only* from them.

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant UI as Chat page
    participant Next as /api/chat (route handler)
    participant API as ChatController
    participant V as Voyage AI
    participant DB as pgvector
    participant C as Claude

    User->>UI: "What are the payment terms?"
    UI->>Next: POST conversation
    Next->>API: Forward + access token
    API->>V: Embed question (input_type=query)
    API->>DB: Top 8 chunks by cosine distance (tenant-scoped)
    API-->>UI: event: sources
    alt No relevant chunks
        API-->>UI: "Couldn't find anything relevant" (no model call)
    else Chunks found
        API->>C: System prompt + history + search_result blocks + question (streaming)
        loop While Claude streams
            C-->>API: text / citations deltas
            API-->>UI: event: block / text / citation
        end
    end
    API-->>UI: event: done
```

How it works in detail ([`ChatService`](src/Backend/DocuMind.Api/Chat/ChatService.cs)):
- **Retrieval:** the question is embedded with Voyage, and pgvector returns the 8 chunks with the smallest cosine distance. An HNSW index keeps this fast. Only `Processed` documents of the current tenant are searched, and searches can optionally be limited to specific documents.
- **Grounding:** each chunk is sent to Claude as a [`search_result`](https://platform.claude.com/docs/en/build-with-claude/search-results) content block whose title names the document and page, with citations enabled. The system prompt tells Claude to answer only from those results, to say when the documents don't cover the question, and to point out conflicts between sources.
- **Citations:** Claude's response arrives as text blocks, each carrying the search results that support it. The API streams these to the browser as server-sent events:

  | Event | Meaning |
  |---|---|
  | `sources` | The retrieved chunks |
  | `block` | A new text span starts |
  | `text` | More text for the current span |
  | `citation` | A source supporting the current span |
  | `done` | The answer is complete |
  | `error` | Something failed |

  The UI turns these into inline **[n]** markers and a source list.
- **Safety and cost:**
  - Refusal fallback is on (`fallbacks: "default"`): if Claude's safety classifiers decline a request, it is re-run on Anthropic's recommended fallback model.
  - Effort is set explicitly to `medium`.
  - If retrieval finds nothing, Claude is not called at all.

### Bring your own key (AI providers)

Each tenant supplies its own API keys on the **Settings** page. One Voyage AI key indexes documents, and at least one of Claude, OpenAI or Gemini answers questions.

```mermaid
flowchart LR
    UI["Settings page"] -- "Server Action" --> API["AiSettingsController"]
    API --> Check{"Verify with provider<br/>(list models / tiny embed)"}
    Check -- "rejected" --> UI
    Check -- "valid" --> Store["ApiKeyStore<br/>encrypt (Data Protection)"]
    Store --> DB[("TenantApiKeys<br/>ciphertext + last 4 chars")]
    DB --> Factory["ChatModelFactory"]
    Factory --> Claude["ClaudeChatModel<br/>native citations"]
    Factory --> OAI["OpenAICompatibleChatModel<br/>OpenAI · Gemini, [n] citations"]
    DB --> Voyage["Voyage embeddings<br/>(worker + retrieval)"]
```

- **Write-only keys:** keys are encrypted with [ASP.NET Core Data Protection](https://learn.microsoft.com/aspnet/core/security/data-protection/introduction) before they reach the database. The API only ever returns a hint such as `…a1b2`, never the key.
- **Verified before saving:** a key is checked with the provider first. Claude, OpenAI and Gemini keys are checked by listing models, which also fills the model picker; Voyage keys by embedding one word. Model choices are checked against that list, so typos are rejected.
- **One interface for every provider:** `ChatModelFactory` builds an `IChatModel` from the tenant's chosen provider, key and model.
  - **Claude** keeps native search-result citations.
  - **OpenAI and Gemini** receive numbered sources in the prompt, are asked to cite them as `[n]`, and the UI turns those markers into the same clickable citations.
- **Fixed embedding model:** every tenant uses its own Voyage key, but always with `voyage-4`. All vectors therefore have the same size and meaning, and the search index stays valid.
- **Client reuse:** SDK clients are cached per key (by a SHA-256 hash of the key), so each request doesn't open new HTTP connections.

> **Production note:** set `DataProtection:KeysPath` (or use a key vault) so the encryption keys survive restarts and are shared between API instances. If the key ring is lost, saved API keys can no longer be decrypted and must be entered again.

### Sign-in, companies and roles

Accounts use [ASP.NET Core Identity](https://learn.microsoft.com/aspnet/core/security/authentication/identity) in the .NET API, with email and password. Each person belongs to one **company** (tenant) with a **role**:

| Role | Can |
|---|---|
| **Admin** | Everything a Member can, plus add or change AI keys (Settings), invite people, change roles and remove members |
| **Member** | Upload and delete documents, ask questions, see the team and which keys are set up |

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Next as Next.js (Server Action)
    participant Proxy as proxy.ts
    participant API as .NET API (Identity)

    User->>Next: Sign up / sign in / accept invitation
    Next->>API: POST /api/auth/register · login · invitations/accept
    API-->>Next: accessToken (30 min) + refreshToken (14 days)
    Next-->>User: Encrypted HttpOnly cookie (AES-GCM, SESSION_SECRET)

    User->>Proxy: Any later request (page, action, chat)
    alt access token expires within 2 minutes
        Proxy->>API: POST /api/auth/refresh
        API-->>Proxy: New tokens with current company and role
        Proxy-->>User: Updated cookie
    else no valid session
        Proxy-->>User: Redirect to /login (or 401 for /api/*)
    end
    Proxy->>Next: Request continues
    Next->>API: Call with Authorization: Bearer token
```

- **Tokens:** Identity's bearer tokens, protected with ASP.NET Core Data Protection.
  - When a token is issued, [`TenantClaimsPrincipalFactory`](src/Backend/DocuMind.Api/Tenancy/TenantClaimsPrincipalFactory.cs) adds the user's company id and role as claims.
  - [`ClaimsTenantProvider`](src/Backend/DocuMind.Api/Tenancy/ClaimsTenantProvider.cs) reads the company id back on each request.
- **Signed in by default:** a fallback authorization policy requires a signed-in user with a company on every endpoint. Only sign-up, sign-in, refresh and invitation lookup are anonymous. Admin-only endpoints use the `TenantAdmin` policy.
- **Revocation:** an access token would normally stay valid until it expires. [`MembershipValidationMiddleware`](src/Backend/DocuMind.Api/Tenancy/MembershipValidationMiddleware.cs) checks the token's company and role against the database on every request (cached for a minute, and cleared when an admin changes someone). A removed member is locked out at once, and a role change applies immediately.
- **Session cookie:** the browser never sees the tokens. [`session.ts`](src/frontend/src/lib/session.ts) keeps them in an AES-GCM-encrypted, `HttpOnly`, `SameSite=Lax` cookie.
  - [`proxy.ts`](src/frontend/src/proxy.ts) (Next.js 16's replacement for middleware) refreshes the tokens before they expire and redirects signed-out visitors.
  - Pages also check the user through a small data access layer ([`auth.ts`](src/frontend/src/lib/auth.ts)), so security never depends on the proxy alone.
- **Invitations:** an Admin enters an email and role and gets a one-time link (`/invite/{token}`) to send. The link expires after 7 days, and only a SHA-256 hash of the token is stored. Opening it shows the company name, and the person sets their name and password to join.
- **Passwords:** at least 10 characters, with no composition rules (following [NIST SP 800-63B](https://pages.nist.gov/800-63-4/sp800-63b.html)). After 5 wrong attempts the account is locked for 5 minutes.
- **Safeguards:**
  - A company always keeps at least one Admin.
  - Admins can't remove themselves.
  - The `?next=` redirect after sign-in only accepts paths on this site.

### Multi-tenancy

Each company is a **tenant**, identified by a `TenantId` (GUID) on every `Document`, `DocumentChunk`, API key, membership and invitation.

- **Query filter:** [`ApplicationDbContext`](src/Backend/DocuMind.Api/Data/ApplicationDbContext.cs) applies an EF Core **global query filter** (`TenantId == current tenant`) to both tables. Every query is scoped automatically, so forgetting a `Where` clause cannot leak another tenant's data.
- **Background work:** the processing worker runs outside any request. It deliberately bypasses the filter (`IgnoreQueryFilters`) and copies the document's `TenantId` onto each chunk itself.
- **Files:** stored under `uploads/{tenantId}/`, and the storage layer refuses any path outside its root (path traversal protection).
- **Current tenant:** comes from the company claim in the signed-in user's token, through `ITenantProvider`. A request without a valid claim gets an empty tenant id that matches no rows, so a mistake returns nothing rather than another company's data.

### Data model

```mermaid
erDiagram
    Tenant ||--o{ TenantMembership : "has members"
    ApplicationUser ||--|| TenantMembership : "belongs to"
    Tenant ||--o{ TenantInvitation : "invites"
    Tenant ||--o{ Document : owns
    Document ||--o{ DocumentChunk : "split into"
    Tenant {
        uuid Id PK
        string Name
        timestamp CreatedAt
    }
    ApplicationUser {
        uuid Id PK
        string Email "also the user name"
        string DisplayName
        string PasswordHash "Identity (PBKDF2)"
        string SecurityStamp "invalidates refresh tokens when changed"
    }
    TenantMembership {
        uuid TenantId PK
        uuid UserId PK "unique: one company per user"
        string Role "Admin | Member"
        timestamp JoinedAt
    }
    TenantInvitation {
        uuid Id PK
        uuid TenantId FK
        string Email
        string Role
        string TokenHash "SHA-256 of the link token"
        timestamp ExpiresAt "7 days"
        timestamp AcceptedAt "nullable"
    }
    Document {
        uuid Id PK
        uuid TenantId "indexed"
        string Title
        string FileName
        bigint FileSizeBytes
        string ContentType
        string StoragePath "uploads/{tenant}/{id}.pdf"
        timestamp UploadedAt
        string Status "Pending | Processing | Processed | Failed"
        timestamp ProcessedAt "nullable"
        string ProcessingError "nullable"
    }
    DocumentChunk {
        uuid Id PK
        uuid TenantId "indexed"
        uuid DocumentId FK "cascade delete"
        int ChunkIndex "unique per document"
        int PageNumber
        text Content
        vector Embedding "vector(1024), HNSW cosine index"
    }
    TenantApiKey {
        uuid Id PK
        uuid TenantId "unique with Provider"
        string Provider "Anthropic | OpenAI | Gemini | Voyage"
        text EncryptedKey "Data Protection ciphertext"
        string KeyHint "last 4 characters"
        string Model "chosen chat model, nullable"
        timestamp UpdatedAt
    }
    TenantSettings {
        uuid TenantId PK
        string ChatProvider "provider that answers questions"
    }
```

Schema changes are managed with EF Core migrations in [`Migrations/`](src/Backend/DocuMind.Api/Migrations).

### Key design decisions

| Decision | Why |
|---|---|
| **Next.js server calls the API, not the browser** | Keeps the API address and access tokens off the client. No CORS is needed in production. |
| **Built-in Identity instead of an external auth provider** | No third-party account or cost, and users stay in our own database. Google or Microsoft sign-in can be added on top later. |
| **Tokens in an encrypted HttpOnly cookie, refreshed in `proxy.ts`** | Page scripts can't steal tokens. Refreshing in the proxy is necessary because cookies can't be written while a page renders. |
| **Membership checked on every request** | Bearer tokens can't be revoked, so the API checks the current membership (cached for a minute) to lock out removed people at once. |
| **pgvector instead of a separate vector database** | One database to run, back up and secure. Vectors and their documents stay consistent through transactions. |
| **Chunks never cross page boundaries** | Every citation can point to an exact page. |
| **Claude search-result blocks with citations** | Citations are produced by the API itself and tied to the exact chunk, so they don't depend on the model formatting references correctly. |
| **Background worker with an in-memory queue** | Uploads return immediately and processing happens afterwards. This is simple for one API instance; the queue is behind an interface so a message broker can replace it later. |
| **Storage behind `IFileStorage`** | Local disk for development; S3 or Azure Blob Storage can be swapped in without touching the controllers. |
| **Secrets in .NET user-secrets and `.env` files** | No passwords or API keys in git. |
| **Tenants bring their own AI keys** | Each company pays for its own AI usage and picks its provider. Keys are encrypted at rest and write-only through the API. |
| **OpenAI and Gemini through one OpenAI-compatible client** | Gemini offers an OpenAI-compatible endpoint, so one implementation covers both. Claude keeps its own implementation for native citations. |

---

## Repository structure

```
documind-ai/
├── docker-compose.yml            # PostgreSQL 17 + pgvector
├── .env.example                  # Template for database credentials (copy to .env)
└── src/
    ├── Backend/
    │   ├── DocuMind.slnx
    │   └── DocuMind.Api/
    │       ├── Program.cs                # Composition root: DI, middleware, configuration
    │       ├── Controllers/
    │       │   ├── DocumentsController.cs  # Upload, list, download, reprocess, delete
    │       │   ├── ChatController.cs       # POST /api/chat → server-sent events
    │       │   ├── AiSettingsController.cs # Bring-your-own-key settings
    │       │   ├── AuthController.cs       # Sign-up, sign-in, refresh, invitations, /me
    │       │   └── TeamController.cs       # Members, roles and invitations (Admin)
    │       ├── ApiKeys/                  # Encrypted key store, key checks, SDK client cache
    │       ├── Chat/                     # Retrieval (pgvector), IChatModel: Claude + OpenAI/Gemini
    │       ├── Processing/               # PDF extraction, chunking, queue, background worker
    │       ├── Embeddings/               # Voyage AI client (IEmbeddingService)
    │       ├── Storage/                  # IFileStorage + local disk implementation
    │       ├── Tenancy/                  # Tenant claims, claims factory, membership check, invitation tokens
    │       ├── Data/                     # ApplicationDbContext (Identity + app tables), query filters
    │       ├── Entities/                 # Users, tenants, memberships, invitations, documents, chunks, keys
    │       ├── Contracts/                # API response shapes
    │       ├── Migrations/               # EF Core migrations
    │       └── DocuMind.Api.http         # Ready-made requests for VS Code / Rider
    └── frontend/
        ├── next.config.ts                # Server Action body limit (51 MB)
        ├── .env.example                  # API URL + session secret
        └── src/
            ├── proxy.ts                  # Session refresh + sign-in redirects (Next.js 16 "proxy")
            ├── lib/
            │   ├── api.ts                # Server-only client for the .NET API (adds the bearer token)
            │   ├── session.ts            # Encrypted session cookie, token refresh
            │   ├── auth.ts               # Data access layer: getCurrentUser, requireUser, requireAdmin
            │   ├── auth-types.ts         # User, team and invitation types
            │   ├── documents.ts          # Shared types and limits
            │   ├── settings.ts           # AI provider settings types
            │   └── chat.ts               # Chat types + SSE stream parser
            └── app/
                ├── layout.tsx, site-header.tsx, user-menu.tsx, setup-banner.tsx
                ├── login/, signup/, invite/[token]/   # Public auth pages
                ├── auth-actions.ts, auth-form.tsx    # Sign-in/up/out Server Actions and shared form
                ├── page.tsx              # Documents: upload form + list
                ├── actions.ts            # Server Actions: upload, delete, reprocess
                ├── chat/                 # "Ask" page and chat UI
                ├── team/                 # Members, roles, invitation links
                ├── settings/             # Paste, verify, replace and remove API keys; pick models (Admin)
                ├── api/chat/route.ts     # Streams chat events from the API
                └── documents/[id]/file/route.ts  # PDF download/inline view
```

---

## Getting started

### Prerequisites

| Tool | Notes |
|---|---|
| [.NET SDK 10](https://dotnet.microsoft.com/download) | `dotnet --version` should print `10.x` |
| [Node.js 20.9+](https://nodejs.org) | Required by Next.js 16 |
| Docker | Docker Desktop, or the lighter Docker Engine inside WSL2 on Windows |
| `dotnet-ef` | `dotnet tool install --global dotnet-ef` |
| API keys | A [Voyage AI](https://dashboard.voyageai.com) key, plus a key for at least one of [Claude](https://platform.claude.com), [OpenAI](https://platform.openai.com) or [Gemini](https://aistudio.google.com). You paste these in the app, not in config. |

### 1. Start the database

```bash
cp .env.example .env               # then set a strong POSTGRES_PASSWORD
docker compose up -d               # on Windows with WSL: wsl -e docker compose up -d
```

### 2. Configure and run the API

```bash
cd src/Backend/DocuMind.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=documind;Username=postgres;Password=<same as .env>"

dotnet ef database update          # creates tables, indexes and the vector extension
dotnet run                         # http://localhost:5288 (API reference: /scalar/v1)
```

### 3. Run the frontend

```bash
cd src/frontend
cp .env.example .env.local         # then set SESSION_SECRET: openssl rand -base64 32
npm install
npm run dev                        # http://localhost:3000
```

### 4. Create your company account

Open <http://localhost:3000>. You'll be sent to sign in, so choose **Create a company account**. You become the company's Admin and land on Settings.

### 5. Add your API keys

1. On **Settings**, paste your **Voyage AI** key and click **Save & verify**.
3. Paste a **Claude**, **OpenAI** or **Gemini** key and pick a model. Claude defaults to `claude-opus-5-5`; for OpenAI and Gemini you choose from the models your key can use.
4. The first chat provider you set up answers questions. Use **Use for answers** to switch.

Then upload a PDF on **Documents**, wait for it to show **Processed**, and ask a question on the **Ask** page. To add colleagues, open **Team**, create an invitation, and send them the link.

---

## Configuration

### Backend (`appsettings.json`, overridable through user-secrets or environment variables)

| Key | Default | Description |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | — | PostgreSQL connection string (keep in user-secrets) |
| `Storage:RootPath` | `uploads` | Folder for uploaded files, relative to the API project |
| `Voyage:Model` | `voyage-4` | Embedding model for every tenant |
| `Voyage:BatchSize` | `128` | Chunks per embedding request |
| `Anthropic:Model` | `claude-opus-5-5` | Default Claude model when a tenant saves a Claude key |
| `Anthropic:Effort` | `medium` | How much effort Claude spends per answer (`low` … `max`) |
| `Anthropic:RetrievalLimit` | `8` | Chunks retrieved per question (all providers) |
| `AiProviders:OpenAIBaseUrl` | `https://api.openai.com/v1/` | OpenAI endpoint (override for a proxy or gateway) |
| `AiProviders:GeminiBaseUrl` | `https://generativelanguage.googleapis.com/v1beta/openai/` | Gemini's OpenAI-compatible endpoint |
| `DataProtection:KeysPath` | *(empty)* | Folder for the keys that encrypt tenants' API keys **and sign-in tokens**. **Set this in production**. Empty uses the OS default store (fine for local development) |
| `Cors:AllowedOrigins` | `http://localhost:3000` (dev) | Origins allowed to call the API directly |
| `Jobs:Mode` | `All` | `Api` (HTTP only), `Worker` (background jobs only) or `All`. Run workers as a separate deployment in production |
| `Jobs:MaxAttempts` / `Jobs:Concurrency` | see `appsettings.json` | Retries before a job is dead-lettered, and parallel jobs per worker. Failed jobs retry with exponential backoff; jobs are idempotent |
| `Storage:Provider` | `Local` | `Local`, `S3` or `AzureBlob`. Provider settings live under `Storage:S3` and `Storage:AzureBlob` |
| `Redis:ConnectionString` | *(empty)* | Optional. Enables the distributed cache, shared Data Protection keys and cluster-wide rate limits. Empty falls back to in-memory |
| `AiLimits:*` | see `appsettings.json` | Max concurrent AI calls, queue length, timeouts and circuit-breaker thresholds for LLM/Voyage calls |
| `RateLimiting:*` / `Quotas:*` | see `appsettings.json` | Per user/IP and per tenant request limits; per-tenant document and daily-upload quotas |
| `ForwardedHeaders:KnownProxies` / `KnownNetworks` | *(empty)* | Addresses of the Next.js server(s) whose `X-Forwarded-For` is trusted, so rate limits see real client IPs. **Leave empty only if the API is not reachable by clients**; otherwise the header is spoofable |
| `Serilog:*` / `OpenTelemetry:OtlpEndpoint` | console only | Structured logs (JSON in production) and OTLP export of traces, metrics and logs. `OTEL_EXPORTER_OTLP_ENDPOINT` also works |
| `RowLevelSecurity:Enabled` | `false` | Sets `app.tenant_id` per connection for the optional Postgres RLS policies (see [docs/architecture.md](docs/architecture.md)) |

Health: `GET /health/live` (process up) and `GET /health/ready` (database reachable). Every response carries `X-Correlation-Id`, which the Next.js server generates and the API forwards into logs, traces and background jobs.

Operations: `DocuMind.Api --migrate` applies migrations and exits (use it as a one-off job). See [docs/deployment.md](docs/deployment.md) for Docker images, the compose `app` profile, the Bicep skeleton and staging notes.

AI provider API keys are **not** configuration: each tenant pastes its own on the Settings page. Token lifetimes (30-minute access, 14-day refresh), password rules and lockout are set in `Program.cs`.

Environment variables use `__` in place of `:`, for example `Anthropic__Model=claude-sonnet-5-5`.

> **Important:** the embedding size (1024) is fixed by the `vector(1024)` column. If you change `Voyage:Model` to one with a different size, add a migration and reprocess all documents.

### Frontend (`src/frontend/.env.local`)

| Variable | Description |
|---|---|
| `DOCUMIND_API_URL` | Base URL of the .NET API (server-side only) |
| `SESSION_SECRET` | Random value of at least 32 characters that encrypts the session cookie (`openssl rand -base64 32`). Changing it signs everyone out |

### Database (`.env` next to `docker-compose.yml`)

`POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`.

---

## API reference

Every endpoint needs an `Authorization: Bearer <accessToken>` header and is scoped to the signed-in user's company. The exceptions are the anonymous auth endpoints marked *public*. **Admin** marks endpoints that need the Admin role (others return `403`). The interactive reference is at `http://localhost:5288/scalar/v1` while running in development.

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/auth/register` | *Public.* Create a company and its Admin (`companyName`, `displayName`, `email`, `password`). Returns `{ accessToken, refreshToken, expiresIn }` |
| `POST` | `/api/auth/login` | *Public.* Sign in (`email`, `password`). Returns tokens. `401` for wrong credentials, `429` when locked out |
| `POST` | `/api/auth/refresh` | *Public.* Exchange a refresh token for new tokens with the current company and role |
| `GET` | `/api/auth/invitations/{token}` | *Public.* Preview an invitation (company, email, role) |
| `POST` | `/api/auth/invitations/accept` | *Public.* Accept an invitation (`token`, `displayName`, `password`). Returns tokens |
| `POST` | `/api/auth/logout` | Revokes the user's outstanding access tokens (rotates the security stamp) |
| `GET` | `/api/auth/me` | The signed-in user, their company and role |
| `GET` | `/api/team/members` | Members of the company |
| `GET` | `/api/team/invitations` | **Admin.** Pending invitations |
| `POST` | `/api/team/invitations` | **Admin.** Invite (`email`, `role`). Returns the one-time `token` for the link |
| `DELETE` | `/api/team/invitations/{id}` | **Admin.** Revoke an invitation |
| `PUT` | `/api/team/members/{userId}/role` | **Admin.** Change a role (`{ "role": "Admin" }`). The last Admin can't be demoted |
| `DELETE` | `/api/team/members/{userId}` | **Admin.** Remove a member (deletes their account) |
| `GET` | `/api/documents` | List documents, newest first |
| `GET` | `/api/documents/{id}` | Get one document's details and status |
| `GET` | `/api/documents/{id}/file` | Download the original PDF |
| `POST` | `/api/documents` | Upload a PDF (`multipart/form-data`: `file`, optional `title`). Returns `201` and queues processing |
| `POST` | `/api/documents/{id}/reprocess` | Queue a document again. Returns `202`, or `409` if it is already queued |
| `DELETE` | `/api/documents/{id}` | Delete the document, its chunks and its file |
| `POST` | `/api/chat` | Ask a question. Body: `{ "messages": [{ "role": "user", "content": "…" }], "documentIds"?: [...], "conversationId"?: "…" }`. Response: a `text/event-stream` whose first event, `conversation`, carries the persisted conversation id |
| `GET` | `/api/conversations` | The signed-in user's saved conversations |
| `GET` | `/api/conversations/{id}` | One conversation with messages and sources |
| `DELETE` | `/api/conversations/{id}` | Delete a conversation |
| `GET` | `/api/activity` | **Admin.** Audit trail (`before`, `limit` for paging) |
| `GET` | `/health/live`, `/health/ready` | *Public.* Liveness and readiness probes |
| `GET` | `/api/settings/ai` | Providers, which are configured (with key hints), chosen models, and the provider that answers |
| `PUT` | `/api/settings/ai/keys/{provider}` | **Admin.** Verify and save a key (`{ "apiKey": "…" }`). Returns the models the key can use. `provider` is `anthropic`, `openai`, `gemini` or `voyage` |
| `GET` | `/api/settings/ai/keys/{provider}/models` | **Admin.** List the chat models the saved key can use |
| `PUT` | `/api/settings/ai/keys/{provider}/model` | **Admin.** Choose a chat model (`{ "model": "…" }`); it must be in the key's model list |
| `PUT` | `/api/settings/ai/chat-provider` | **Admin.** Choose which provider answers (`{ "provider": "openai" }`) |
| `DELETE` | `/api/settings/ai/keys/{provider}` | **Admin.** Remove a saved key |

Errors use the standard [Problem Details](https://www.rfc-editor.org/rfc/rfc9457) JSON format. Example requests for every endpoint are in [`DocuMind.Api.http`](src/Backend/DocuMind.Api/DocuMind.Api.http).

---

## Development workflow

| Task | Command |
|---|---|
| Build the API | `dotnet build` (in `src/Backend`) |
| Add a migration | `dotnet ef migrations add <Name>` (in `src/Backend/DocuMind.Api`) |
| Apply migrations | `dotnet ef database update` |
| Lint the frontend | `npx eslint .` (in `src/frontend`) |
| Backend tests | `dotnet test` (in `src/Backend`). Integration tests use Testcontainers (Postgres + pgvector) and are skipped when Docker is unavailable, unless `CI=true` |
| Frontend tests | `npm test` (in `src/frontend`) |
| Full stack in Docker | `docker compose --profile app up --build` |
| Production build of the frontend | `npm run build` |
| Inspect the database | `docker exec -it documind_db psql -U postgres -d documind` |

Branches: new work happens on feature branches (for example `feature/byok-api-keys`) and is merged into `main` once it builds and has been tested.

---

## Known limitations

- **Invitations aren't emailed:** the Admin copies the invitation link and sends it themselves. There is also no password reset or email verification for self sign-up yet; both need an email service.
- **One company per account:** an email can belong to one company. Inviting an address that already has an account is refused.
- **Sign-out revocation is eventually consistent:** `POST /api/auth/logout` rotates the user's security stamp, which invalidates their tokens, but each API instance caches membership for up to a minute (the local instance is cleared immediately). There are no refresh-token families or per-device sessions.
- **Rate limiting depends on forwarded IPs:** configure `ForwardedHeaders:KnownProxies`/`KnownNetworks` for the Next.js server, otherwise limits fall back to the proxy's address (or trust a spoofable header). Limits are in-memory per instance unless Redis is configured; the Redis limiter fails open if Redis is down.
- **Citation quality differs by provider:** Claude's citations come from the API and point at the exact chunk. OpenAI and Gemini rely on the model writing `[n]` markers, which they occasionally skip or misnumber.
- **Scanned PDFs:** image-only PDFs contain no extractable text and are marked *Failed*. OCR is not supported yet.
- **Unverified against real infrastructure:** the HNSW/GIN index migrations, hybrid search, Testcontainers integration tests, S3/Azure Blob storage, Redis features and the Bicep skeleton were written and compiled but not run against live pgvector, Docker, AWS, Azure or Redis in the environment where they were authored. CI runs the Postgres-backed tests; the rest need a staging environment.
- **Index rebuild:** the `AddRetrievalIndexes` migration recreates the HNSW index, which blocks writes on large tables. Run it off-peak.
- **Chat UI:** the API persists conversations and uses them for retrieval, but the frontend only continues the current thread; there is no conversation list or history page yet. Answers render as plain text.
- **Audits still use vector-only retrieval:** only chat uses hybrid search.
- **Row-level security is optional and manual:** apply the SQL script yourself and run the app as a non-owner role; it is off by default.

---

## Roadmap

- [x] Project setup: Next.js 16 frontend, .NET 10 API, PostgreSQL + pgvector in Docker
- [x] Document upload, listing, download and deletion with tenant scoping
- [x] Processing pipeline: PDF text extraction, chunking, Voyage embeddings, background worker
- [x] Question answering with Claude, streaming and page-level citations
- [x] Bring your own key: per-tenant encrypted keys for Claude, OpenAI, Gemini and Voyage AI
- [x] Sign-in and companies: email and password accounts, Admin and Member roles, invitation links, Team page
- [ ] **Compliance and risk audits:** run configurable checklists (for example "has an indemnity clause", "termination notice ≥ 60 days") against each document with Claude and store the findings
- [x] **Scalability roadmap:** durable Postgres job queue with retries and dead-lettering, separate worker mode, S3/Azure Blob storage, optional Redis, HNSW + full-text hybrid retrieval, rate limits and quotas, circuit breakers, token revocation, OpenTelemetry/Serilog, health probes, tests and CI, Dockerfiles, migration job, Bicep skeleton, persisted chat history (API), activity log, optional RLS
- [ ] Chat history UI (conversation list), Markdown rendering
- [ ] Email delivery for invitations, password reset and email verification; optional Google and Microsoft sign-in
- [ ] OCR for scanned PDFs
- [ ] Hybrid retrieval for audits; per-tenant embedding-cost metering
- [ ] Validate the Azure/AWS infrastructure and load-test in a staging environment
