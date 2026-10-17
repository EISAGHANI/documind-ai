# DocuMind AI

DocuMind AI is a multi-tenant B2B SaaS platform for working with large collections of business documents. Companies upload PDFs such as contracts, compliance records and technical manuals. DocuMind extracts their text, indexes it for semantic (vector) search, and lets people ask questions in plain language. Answers come from Claude and cite the exact document and page they're based on.

> **Status:** early development. Uploading, processing, vector search, question answering with citations, and bring-your-own-key settings work end to end. Sign-in, compliance audits and production deployment are not built yet (see the [Roadmap](#roadmap)).

---

## Contents

- [What it does](#what-it-does)
- [Technology stack](#technology-stack)
- [Architecture](#architecture)
  - [System overview](#system-overview)
  - [Document ingestion pipeline](#document-ingestion-pipeline)
  - [Question answering (RAG)](#question-answering-rag)
  - [Bring your own key (AI providers)](#bring-your-own-key-ai-providers)
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
| **Tenant isolation** | Every document, chunk and search is scoped to one tenant (company), and the database layer enforces this automatically. |

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
    Pages & Actions & Routes -- "HTTP + X-Tenant-Id" --> DocsCtl & ChatCtl
    DocsCtl --> Disk & DB
    DocsCtl -- enqueue --> Queue --> Worker
    Worker --> Disk & DB & Voyage
    ChatCtl --> DB & Voyage & Claude
```

The system has three parts:

1. **Next.js (backend-for-frontend).** The browser only talks to the Next.js server. Next.js renders the document list on the server, handles uploads and deletes through **Server Actions**, and forwards the chat and download streams through **route handlers**. The .NET API's address and the tenant header stay on the server, which is also where user access tokens will go once sign-in exists.
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
    Next->>API: Forward + X-Tenant-Id
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

### Multi-tenancy

Each company is a **tenant**, identified by a `TenantId` (GUID) on every `Document` and `DocumentChunk`.

- **Query filter:** [`ApplicationDbContext`](src/Backend/DocuMind.Api/Data/ApplicationDbContext.cs) applies an EF Core **global query filter** (`TenantId == current tenant`) to both tables. Every query is scoped automatically, so forgetting a `Where` clause cannot leak another tenant's data.
- **Background work:** the processing worker runs outside any request. It deliberately bypasses the filter (`IgnoreQueryFilters`) and copies the document's `TenantId` onto each chunk itself.
- **Files:** stored under `uploads/{tenantId}/`, and the storage layer refuses any path outside its root (path traversal protection).
- **Current tenant (temporary):** for now the tenant comes from an `X-Tenant-Id` header that the Next.js server adds, with a fixed development tenant as fallback ([`HeaderTenantProvider`](src/Backend/DocuMind.Api/Tenancy/HeaderTenantProvider.cs)). Once sign-in is added, it will come from the signed-in user's token instead. Nothing else needs to change, because everything depends on the `ITenantProvider` interface.

### Data model

```mermaid
erDiagram
    Document ||--o{ DocumentChunk : "split into"
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
| **Next.js server calls the API, not the browser** | Keeps the API address, tenant header and (later) access tokens off the client. No CORS is needed in production. |
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
    │       │   └── AiSettingsController.cs # Bring-your-own-key settings
    │       ├── ApiKeys/                  # Encrypted key store, key checks, SDK client cache
    │       ├── Chat/                     # Retrieval (pgvector), IChatModel: Claude + OpenAI/Gemini
    │       ├── Processing/               # PDF extraction, chunking, queue, background worker
    │       ├── Embeddings/               # Voyage AI client (IEmbeddingService)
    │       ├── Storage/                  # IFileStorage + local disk implementation
    │       ├── Tenancy/                  # ITenantProvider (header-based for now)
    │       ├── Data/                     # ApplicationDbContext, query filters, indexes
    │       ├── Entities/                 # Document, DocumentChunk, TenantApiKey, TenantSettings
    │       ├── Contracts/                # API response shapes
    │       ├── Migrations/               # EF Core migrations
    │       └── DocuMind.Api.http         # Ready-made requests for VS Code / Rider
    └── frontend/
        ├── next.config.ts                # Server Action body limit (51 MB)
        ├── .env.example                  # API URL + development tenant id
        └── src/
            ├── lib/
            │   ├── api.ts                # Server-only client for the .NET API
            │   ├── documents.ts          # Shared types and limits
            │   ├── settings.ts           # AI provider settings types
            │   └── chat.ts               # Chat types + SSE stream parser
            └── app/
                ├── layout.tsx, site-header.tsx, setup-banner.tsx
                ├── page.tsx              # Documents: upload form + list
                ├── actions.ts            # Server Actions: upload, delete, reprocess
                ├── chat/                 # "Ask" page and chat UI
                ├── settings/             # Paste, verify, replace and remove API keys; pick models
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
cp .env.example .env.local
npm install
npm run dev                        # http://localhost:3000
```

### 4. Add your API keys

1. Open <http://localhost:3000/settings>.
2. Paste your **Voyage AI** key and click **Save & verify**.
3. Paste a **Claude**, **OpenAI** or **Gemini** key and pick a model. Claude defaults to `claude-opus-5-5`; for OpenAI and Gemini you choose from the models your key can use.
4. The first chat provider you set up answers questions. Use **Use for answers** to switch.

Then upload a PDF on **Documents**, wait for it to show **Processed**, and ask a question on the **Ask** page.

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
| `DataProtection:KeysPath` | *(empty)* | Folder for the keys that encrypt tenants' API keys. **Set this in production**. Empty uses the OS default store (fine for local development) |

AI provider API keys are **not** configuration: each tenant pastes its own on the Settings page.
| `Cors:AllowedOrigins` | `http://localhost:3000` (dev) | Origins allowed to call the API directly |
| `Tenancy:DevelopmentTenantId` | `…0001` (dev) | Tenant used when no `X-Tenant-Id` header is sent |

Environment variables use `__` in place of `:`, for example `Anthropic__Model=claude-sonnet-5-5`.

> **Important:** the embedding size (1024) is fixed by the `vector(1024)` column. If you change `Voyage:Model` to one with a different size, add a migration and reprocess all documents.

### Frontend (`src/frontend/.env.local`)

| Variable | Description |
|---|---|
| `DOCUMIND_API_URL` | Base URL of the .NET API (server-side only) |
| `DOCUMIND_TENANT_ID` | Tenant sent as `X-Tenant-Id` until sign-in exists |

### Database (`.env` next to `docker-compose.yml`)

`POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`.

---

## API reference

All endpoints are scoped to the current tenant. The interactive reference is at `http://localhost:5288/scalar/v1` while running in development.

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/documents` | List documents, newest first |
| `GET` | `/api/documents/{id}` | Get one document's details and status |
| `GET` | `/api/documents/{id}/file` | Download the original PDF |
| `POST` | `/api/documents` | Upload a PDF (`multipart/form-data`: `file`, optional `title`). Returns `201` and queues processing |
| `POST` | `/api/documents/{id}/reprocess` | Queue a document again. Returns `202`, or `409` if it is already queued |
| `DELETE` | `/api/documents/{id}` | Delete the document, its chunks and its file |
| `POST` | `/api/chat` | Ask a question. Body: `{ "messages": [{ "role": "user", "content": "…" }], "documentIds"?: [...] }`. Response: a `text/event-stream` |
| `GET` | `/api/settings/ai` | Providers, which are configured (with key hints), chosen models, and the provider that answers |
| `PUT` | `/api/settings/ai/keys/{provider}` | Verify and save a key (`{ "apiKey": "…" }`). Returns the models the key can use. `provider` is `anthropic`, `openai`, `gemini` or `voyage` |
| `GET` | `/api/settings/ai/keys/{provider}/models` | List the chat models the saved key can use |
| `PUT` | `/api/settings/ai/keys/{provider}/model` | Choose a chat model (`{ "model": "…" }`); it must be in the key's model list |
| `PUT` | `/api/settings/ai/chat-provider` | Choose which provider answers (`{ "provider": "openai" }`) |
| `DELETE` | `/api/settings/ai/keys/{provider}` | Remove a saved key |

Errors use the standard [Problem Details](https://www.rfc-editor.org/rfc/rfc9457) JSON format. Example requests for every endpoint are in [`DocuMind.Api.http`](src/Backend/DocuMind.Api/DocuMind.Api.http).

---

## Development workflow

| Task | Command |
|---|---|
| Build the API | `dotnet build` (in `src/Backend`) |
| Add a migration | `dotnet ef migrations add <Name>` (in `src/Backend/DocuMind.Api`) |
| Apply migrations | `dotnet ef database update` |
| Lint the frontend | `npx eslint .` (in `src/frontend`) |
| Production build of the frontend | `npm run build` |
| Inspect the database | `docker exec -it documind_db psql -U postgres -d documind` |

Branches: new work happens on feature branches (for example `feature/byok-api-keys`) and is merged into `main` once it builds and has been tested.

---

## Known limitations

- **No sign-in yet:** the tenant comes from a header and a development fallback. Don't expose the API publicly in this state. Anyone who can reach it could replace a tenant's API keys. Once sign-in exists, Settings must be limited to tenant admins.
- **Citation quality differs by provider:** Claude's citations come from the API and point at the exact chunk. OpenAI and Gemini rely on the model writing `[n]` markers, which they occasionally skip or misnumber.
- **Scanned PDFs:** image-only PDFs contain no extractable text and are marked *Failed*. OCR is not supported yet.
- **One API instance:** the in-memory processing queue doesn't survive across multiple instances; a message broker is needed to scale out.
- **Chats aren't saved:** conversations disappear on page refresh, and only the latest question is used for retrieval.
- **Formatting:** answers render as plain text, so Markdown formatting isn't shown as formatting yet.
- **Local file storage:** fine for development; production needs cloud storage (S3 or Azure Blob).

---

## Roadmap

- [x] Project setup: Next.js 16 frontend, .NET 10 API, PostgreSQL + pgvector in Docker
- [x] Document upload, listing, download and deletion with tenant scoping
- [x] Processing pipeline: PDF text extraction, chunking, Voyage embeddings, background worker
- [x] Question answering with Claude, streaming and page-level citations
- [x] Bring your own key: per-tenant encrypted keys for Claude, OpenAI, Gemini and Voyage AI
- [ ] **Compliance and risk audits:** run configurable checklists (for example "has an indemnity clause", "termination notice ≥ 60 days") against each document with Claude and store the findings
- [ ] **Sign-in and real tenants:** organizations, users and roles; tenant taken from the signed-in user's token
- [ ] Saved chat history and Markdown rendering
- [ ] OCR for scanned PDFs
- [ ] Cloud file storage and a durable job queue
- [ ] Tests, CI/CD, containerized deployment and observability
