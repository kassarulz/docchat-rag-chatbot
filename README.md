# DocChat – RAG chatbot (React + ASP.NET Core + Claude)

A retrieval-augmented generation (RAG) chatbot that answers questions **only from your own documents**, with numbered citations you can click to check the source passage.

- **Frontend:** React 19 + TypeScript (Vite), streaming chat UI, drag-and-drop uploads, light/dark themes, mobile layout
- **Backend:** ASP.NET Core (.NET 10) minimal API, in-memory document store, BM25 retrieval, Server-Sent Events streaming
- **LLM:** Claude (`claude-opus-5-5`) via the official Anthropic C# SDK

The repo ships with three sample documents describing *Harvestly*, a fictional farm management app, so the demo works straight away.

## How it works

```
 Browser (React)                     ASP.NET Core API                         Claude API
 ───────────────                     ────────────────                         ──────────
 upload .md/.txt  ──POST /api/documents──▶ split into ~900-char chunks
                                          build BM25 keyword index
 ask a question   ──POST /api/chat──────▶ 1. retrieve top 5 chunks (BM25)
                  ◀── event: sources ──── 2. send sources to the UI
                                          3. prompt = system rules + numbered sources + question ──▶
                  ◀── event: delta ─────── 4. stream the answer token by token ◀─────────────────────
 [1] [2] citation chips link to the source passages
```

1. **Chunking.** Documents are split on paragraph boundaries into ~900-character chunks, with a short overlap so an answer that spans a paragraph break still retrieves together. Headings stay with the text below them.
2. **Retrieval.** A BM25 keyword index (the ranking used by most search engines) scores each chunk against the question. Follow-up questions also include the previous question, so "what about cucumbers?" still finds the right passages. No embeddings service is needed, so the whole demo runs on one API key.
3. **Grounded generation.** The top 5 chunks are sent to Claude as numbered `<source>` blocks. The system prompt tells the model to answer only from the sources, cite them as `[n]`, and say so when the answer isn't there instead of guessing.
4. **Streaming.** The API streams Server-Sent Events: a `sources` event first, then `delta` text chunks, then `done`. The UI renders Markdown as it arrives and turns `[n]` into clickable citation chips.

### Reliability and safety details

- The API key stays on the server; the browser only talks to `/api/*` (proxied by Vite in development).
- Uploads are limited to text formats (`.txt`, `.md`, `.csv`, `.json`) and 2 MB per file.
- Typed SDK exceptions map to clear messages: invalid key, rate limit and API errors are reported separately.
- Server-side fallback is enabled (`fallbacks: default`), so if a safety classifier declines a request, it is re-served by a fallback model in the same call. Refusals and length cut-offs are shown to the user as notices.
- The user can stop generation at any time; the request is cancelled end to end.

## Run it locally

**Requirements:** .NET 10 SDK, Node.js 20+, and an Anthropic API key.

### 1. Backend

```bash
cd backend
export ANTHROPIC_API_KEY=sk-ant-...      # PowerShell: $env:ANTHROPIC_API_KEY="sk-ant-..."
dotnet run
```

The API listens on `http://localhost:5080`. The project folder includes a `nuget.config` that restores packages from nuget.org.

### 2. Frontend

```bash
cd frontend
npm install
npm run dev
```

Open `http://localhost:5173`. Try one of the suggested questions, or upload your own documents.

Without an API key, the app still loads, indexes documents and shows the retrieved sources, with a banner explaining that answers need a key.

## API

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/health` | Status, and whether an API key is configured |
| `GET` | `/api/documents` | List indexed documents |
| `POST` | `/api/documents` | Upload one or more files (`multipart/form-data`, field `files`) |
| `DELETE` | `/api/documents/{id}` | Remove a document from the index |
| `GET` | `/api/search?q=...` | Retrieval only (no model call): top 5 chunks with scores |
| `POST` | `/api/chat` | `{ "messages": [{ "role": "user", "content": "..." }] }` → SSE stream |

## Project structure

```
RagChatbot/
├── backend/                     ASP.NET Core minimal API
│   ├── Program.cs               endpoints, SSE streaming, error handling
│   ├── Rag/DocumentStore.cs     chunking + BM25 index
│   ├── Rag/ChatService.cs       retrieval → prompt → Claude streaming
│   └── SampleDocs/              fictional Harvestly docs loaded at startup
└── frontend/                    React + TypeScript (Vite)
    ├── src/App.tsx              chat UI, uploads, citations
    ├── src/api.ts               typed API client + SSE parser
    └── src/index.css            theme tokens (light/dark), responsive layout
```

## Where to take it next

- **Persistent storage:** store chunks in PostgreSQL (with `pgvector`) or Azure AI Search instead of memory.
- **Semantic search:** add embeddings (for example Voyage AI) and combine them with BM25 for hybrid retrieval.
- **More formats:** PDF and Word parsing, or send PDFs to Claude directly as document blocks.
- **Auth and multi-tenancy:** per-user knowledge bases behind ASP.NET Core Identity or Entra ID.
