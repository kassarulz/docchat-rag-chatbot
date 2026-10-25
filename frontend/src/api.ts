export interface DocumentInfo {
  id: string
  name: string
  chunks: number
  characters: number
  isSample: boolean
}

export interface SourceRef {
  number: number
  documentId: string
  documentName: string
  chunkIndex: number
  excerpt: string
  score: number
}

export interface ChatTurn {
  role: 'user' | 'assistant'
  content: string
}

export interface Health {
  status: string
  apiKeyConfigured: boolean
}

async function readError(res: Response): Promise<string> {
  try {
    const body = await res.json()
    return body.error ?? `Request failed (${res.status})`
  } catch {
    return `Request failed (${res.status})`
  }
}

export async function getHealth(): Promise<Health> {
  const res = await fetch('/api/health')
  if (!res.ok) throw new Error(await readError(res))
  return res.json()
}

export async function listDocuments(): Promise<DocumentInfo[]> {
  const res = await fetch('/api/documents')
  if (!res.ok) throw new Error(await readError(res))
  return res.json()
}

export async function uploadDocuments(files: File[]): Promise<DocumentInfo[]> {
  const form = new FormData()
  files.forEach((f) => form.append('files', f))
  const res = await fetch('/api/documents', { method: 'POST', body: form })
  if (!res.ok) throw new Error(await readError(res))
  return res.json()
}

export async function deleteDocument(id: string): Promise<void> {
  const res = await fetch(`/api/documents/${id}`, { method: 'DELETE' })
  if (!res.ok && res.status !== 404) throw new Error(await readError(res))
}

export interface ChatHandlers {
  onSources: (sources: SourceRef[]) => void
  onDelta: (text: string) => void
  onNotice: (message: string) => void
  onError: (message: string) => void
}

/** POSTs the conversation and reads the Server-Sent Events stream until "done". */
export async function streamChat(messages: ChatTurn[], handlers: ChatHandlers, signal: AbortSignal): Promise<void> {
  const res = await fetch('/api/chat', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ messages }),
    signal,
  })
  if (!res.ok || !res.body) throw new Error(await readError(res))

  const reader = res.body.pipeThrough(new TextDecoderStream()).getReader()
  let buffer = ''

  while (true) {
    const { value, done } = await reader.read()
    if (done) break
    buffer += value

    let boundary: number
    while ((boundary = buffer.indexOf('\n\n')) !== -1) {
      const raw = buffer.slice(0, boundary)
      buffer = buffer.slice(boundary + 2)

      let event = 'message'
      let data = ''
      for (const line of raw.split('\n')) {
        if (line.startsWith('event: ')) event = line.slice(7)
        else if (line.startsWith('data: ')) data += line.slice(6)
      }
      if (!data) continue
      const payload = JSON.parse(data)

      switch (event) {
        case 'sources': handlers.onSources(payload); break
        case 'delta': handlers.onDelta(payload.text); break
        case 'notice': handlers.onNotice(payload.message); break
        case 'error': handlers.onError(payload.message); break
        case 'done': return
      }
    }
  }
}
