import { useCallback, useEffect, useRef, useState } from 'react'
import Markdown from 'react-markdown'
import {
  deleteDocument, getHealth, listDocuments, streamChat, uploadDocuments,
  type ChatTurn, type DocumentInfo, type SourceRef,
} from './api'

interface Message {
  id: string
  role: 'user' | 'assistant'
  content: string
  sources?: SourceRef[]
  notice?: string
  error?: string
  streaming?: boolean
}

const SUGGESTIONS = [
  'How is fertilizer cost split between tunnels?',
  'What can a Worker do compared with a Manager?',
  'What is the recommended EC and pH for tomatoes?',
  'What happens when the free trial ends?',
]

const ACCEPTED = '.txt,.md,.markdown,.csv,.json'

// Turns "[2]" citations into links the Markdown renderer can style and make clickable.
function linkCitations(text: string): string {
  return text.replace(/\[(\d{1,2})\](?!\()/g, '[$1](#source-$1)')
}

export default function App() {
  const [documents, setDocuments] = useState<DocumentInfo[]>([])
  const [messages, setMessages] = useState<Message[]>([])
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [docError, setDocError] = useState<string | null>(null)
  const [apiKeyConfigured, setApiKeyConfigured] = useState<boolean | null>(null)
  const [backendDown, setBackendDown] = useState(false)
  const [dragOver, setDragOver] = useState(false)
  const [highlight, setHighlight] = useState<string | null>(null)
  const [sidebarOpen, setSidebarOpen] = useState(false)

  const abortRef = useRef<AbortController | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const scrollRef = useRef<HTMLDivElement>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const refreshDocuments = useCallback(async () => {
    try {
      setDocuments(await listDocuments())
      setBackendDown(false)
    } catch {
      setBackendDown(true)
    }
  }, [])

  useEffect(() => {
    refreshDocuments()
    getHealth().then((h) => setApiKeyConfigured(h.apiKeyConfigured)).catch(() => setBackendDown(true))
  }, [refreshDocuments])

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' })
  }, [messages])

  const updateMessage = (id: string, patch: (m: Message) => Partial<Message>) =>
    setMessages((prev) => prev.map((m) => (m.id === id ? { ...m, ...patch(m) } : m)))

  async function send(text: string) {
    const question = text.trim()
    if (!question || busy) return

    const userMsg: Message = { id: crypto.randomUUID(), role: 'user', content: question }
    const assistantId = crypto.randomUUID()
    const history: ChatTurn[] = [...messages, userMsg]
      .filter((m) => !m.error && m.content)
      .map((m) => ({ role: m.role, content: m.content }))

    setMessages((prev) => [...prev, userMsg, { id: assistantId, role: 'assistant', content: '', streaming: true }])
    setInput('')
    setBusy(true)

    const controller = new AbortController()
    abortRef.current = controller
    try {
      await streamChat(history, {
        onSources: (sources) => updateMessage(assistantId, () => ({ sources })),
        onDelta: (delta) => updateMessage(assistantId, (m) => ({ content: m.content + delta })),
        onNotice: (notice) => updateMessage(assistantId, () => ({ notice })),
        onError: (error) => updateMessage(assistantId, () => ({ error })),
      }, controller.signal)
    } catch (err) {
      if (!controller.signal.aborted) {
        updateMessage(assistantId, () => ({ error: err instanceof Error ? err.message : 'Request failed.' }))
      }
    } finally {
      updateMessage(assistantId, (m) => ({
        streaming: false,
        notice: controller.signal.aborted && !m.notice ? 'Stopped.' : m.notice,
      }))
      setBusy(false)
      abortRef.current = null
      textareaRef.current?.focus()
    }
  }

  async function handleFiles(fileList: FileList | File[] | null) {
    const files = Array.from(fileList ?? [])
    if (files.length === 0) return
    setUploading(true)
    setDocError(null)
    try {
      await uploadDocuments(files)
      await refreshDocuments()
    } catch (err) {
      setDocError(err instanceof Error ? err.message : 'Upload failed.')
    } finally {
      setUploading(false)
      if (fileInputRef.current) fileInputRef.current.value = ''
    }
  }

  async function removeDocument(id: string) {
    setDocError(null)
    try {
      await deleteDocument(id)
      await refreshDocuments()
    } catch (err) {
      setDocError(err instanceof Error ? err.message : 'Could not remove the document.')
    }
  }

  function focusSource(messageId: string, n: string) {
    const key = `${messageId}-${n}`
    setHighlight(key)
    document.getElementById(`src-${key}`)?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
    window.setTimeout(() => setHighlight((h) => (h === key ? null : h)), 1800)
  }

  const totalChunks = documents.reduce((sum, d) => sum + d.chunks, 0)

  return (
    <div className="app">
      <aside className={`sidebar ${sidebarOpen ? 'open' : ''}`}>
        <div className="brand">
          <span className="brand-mark" aria-hidden>
            <svg viewBox="0 0 24 24" width="18" height="18"><path fill="currentColor" d="M5 5h14v9a3 3 0 0 1-3 3h-5l-4 3v-3a2 2 0 0 1-2-2z" /></svg>
          </span>
          <div>
            <div className="brand-name">DocChat</div>
            <div className="brand-sub">Answers from your documents</div>
          </div>
        </div>

        <div className="section-head">
          <h2>Knowledge base</h2>
          <span className="muted small">{documents.length} docs · {totalChunks} chunks</span>
        </div>

        <div
          className={`dropzone ${dragOver ? 'over' : ''}`}
          onDragOver={(e) => { e.preventDefault(); setDragOver(true) }}
          onDragLeave={() => setDragOver(false)}
          onDrop={(e) => { e.preventDefault(); setDragOver(false); handleFiles(e.dataTransfer.files) }}
          onClick={() => fileInputRef.current?.click()}
          role="button"
          tabIndex={0}
          onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') fileInputRef.current?.click() }}
        >
          <input ref={fileInputRef} type="file" multiple accept={ACCEPTED} hidden onChange={(e) => handleFiles(e.target.files)} />
          <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden><path fill="currentColor" d="M11 16V7.8l-3.6 3.6L6 10l6-6 6 6-1.4 1.4L13 7.8V16zm-6 4v-5h2v3h10v-3h2v5z" /></svg>
          <span>{uploading ? 'Indexing…' : 'Drop files or click to upload'}</span>
          <span className="muted small">.txt, .md, .csv, .json · up to 2 MB</span>
        </div>

        {docError && <div className="alert error small">{docError}</div>}

        <ul className="doc-list">
          {documents.map((d) => (
            <li key={d.id} className="doc">
              <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden className="doc-icon"><path fill="currentColor" d="M6 2h8l6 6v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2m7 1.5V9h5.5z" /></svg>
              <div className="doc-meta">
                <span className="doc-name" title={d.name}>{d.name}</span>
                <span className="muted small">{d.chunks} chunks{d.isSample ? ' · sample' : ''}</span>
              </div>
              <button className="icon-btn" aria-label={`Remove ${d.name}`} title="Remove" onClick={() => removeDocument(d.id)}>
                <svg viewBox="0 0 24 24" width="16" height="16"><path fill="currentColor" d="M18.3 5.7 12 12l6.3 6.3-1.4 1.4L10.6 13.4 4.3 19.7 2.9 18.3 9.2 12 2.9 5.7l1.4-1.4 6.3 6.3 6.3-6.3z" /></svg>
              </button>
            </li>
          ))}
          {documents.length === 0 && !backendDown && <li className="muted small empty">No documents yet. Upload some to get started.</li>}
        </ul>

        <footer className="sidebar-foot muted small">
          Retrieval: BM25 over ~900-char chunks · Generation: Claude via ASP.NET Core
        </footer>
      </aside>

      {sidebarOpen && <div className="scrim" onClick={() => setSidebarOpen(false)} />}

      <main className="chat">
        <header className="chat-head">
          <button className="icon-btn menu" aria-label="Show documents" onClick={() => setSidebarOpen(true)}>
            <svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M3 6h18v2H3zm0 5h18v2H3zm0 5h18v2H3z" /></svg>
          </button>
          <h1>Ask your documents</h1>
          {messages.length > 0 && (
            <button className="ghost-btn" onClick={() => setMessages([])} disabled={busy}>New chat</button>
          )}
        </header>

        {backendDown && (
          <div className="alert error">Can't reach the backend. Start it with <code>dotnet run</code> in the <code>backend</code> folder.</div>
        )}
        {apiKeyConfigured === false && !backendDown && (
          <div className="alert warn">
            <strong>ANTHROPIC_API_KEY isn't set</strong> on the backend. Documents and search work, but answers need a key. Set it and restart the backend.
          </div>
        )}

        <div className="messages" ref={scrollRef}>
          {messages.length === 0 ? (
            <div className="welcome">
              <h2>What would you like to know?</h2>
              <p className="muted">
                Answers come only from the documents in your knowledge base, with numbered citations you can check.
                The sample docs describe <em>Harvestly</em>, a fictional farm management app.
              </p>
              <div className="suggestions">
                {SUGGESTIONS.map((s) => (
                  <button key={s} className="suggestion" onClick={() => send(s)} disabled={busy}>{s}</button>
                ))}
              </div>
            </div>
          ) : (
            messages.map((m) => (
              <div key={m.id} className={`msg ${m.role}`}>
                {m.role === 'user' ? (
                  <div className="bubble">{m.content}</div>
                ) : (
                  <div className="answer">
                    {m.content ? (
                      <div className="prose">
                        <Markdown
                          components={{
                            a: ({ href, children }) => {
                              const n = href?.match(/^#source-(\d+)$/)?.[1]
                              if (n) {
                                return (
                                  <button className="cite" onClick={() => focusSource(m.id, n)} title={m.sources?.[Number(n) - 1]?.documentName}>
                                    {n}
                                  </button>
                                )
                              }
                              return <a href={href} target="_blank" rel="noreferrer">{children}</a>
                            },
                          }}
                        >
                          {linkCitations(m.content)}
                        </Markdown>
                        {m.streaming && <span className="caret" aria-hidden />}
                      </div>
                    ) : m.streaming && !m.error ? (
                      <div className="thinking"><span /><span /><span /><em className="muted small">Searching documents…</em></div>
                    ) : null}

                    {m.error && <div className="alert error small">{m.error}</div>}
                    {m.notice && <div className="alert info small">{m.notice}</div>}

                    {m.sources && m.sources.length > 0 && (m.content || !m.streaming) && (
                      <details className="sources" open={!m.streaming}>
                        <summary>{m.sources.length} sources</summary>
                        <ol>
                          {m.sources.map((s) => {
                            const key = `${m.id}-${s.number}`
                            return (
                              <li key={key} id={`src-${key}`} className={highlight === key ? 'flash' : ''}>
                                <span className="src-num">{s.number}</span>
                                <div>
                                  <div className="src-doc">{s.documentName} <span className="muted small">· chunk {s.chunkIndex + 1}</span></div>
                                  <div className="src-excerpt muted small">{s.excerpt}</div>
                                </div>
                              </li>
                            )
                          })}
                        </ol>
                      </details>
                    )}
                    {m.sources && m.sources.length === 0 && !m.streaming && (
                      <div className="muted small">No matching passages were found in your documents.</div>
                    )}
                  </div>
                )}
              </div>
            ))
          )}
        </div>

        <form className="composer" onSubmit={(e) => { e.preventDefault(); send(input) }}>
          <textarea
            ref={textareaRef}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(input) }
            }}
            placeholder="Ask a question about your documents…"
            rows={1}
            disabled={backendDown}
          />
          {busy ? (
            <button type="button" className="send stop" onClick={() => abortRef.current?.abort()} aria-label="Stop generating">
              <svg viewBox="0 0 24 24" width="18" height="18"><rect x="6" y="6" width="12" height="12" rx="2" fill="currentColor" /></svg>
            </button>
          ) : (
            <button type="submit" className="send" disabled={!input.trim() || backendDown} aria-label="Send">
              <svg viewBox="0 0 24 24" width="18" height="18"><path fill="currentColor" d="M3 20.5 21 12 3 3.5 3 10l12 2-12 2z" /></svg>
            </button>
          )}
        </form>
      </main>
    </div>
  )
}
