/**
 * Small fetch wrapper for the VueTicket API.
 * - The access token lives in memory only (never localStorage).
 * - The refresh token is an httpOnly cookie the browser sends to /api/v1/auth/*.
 * - On a 401 it refreshes once (shared by all requests in flight) and retries.
 */

const BASE = '/api/v1'

export class ApiError extends Error {
  readonly status: number
  readonly code: string

  constructor(status: number, code: string, message: string) {
    super(message)
    this.status = status
    this.code = code
  }
}

let accessToken: string | null = null
let refreshing: Promise<boolean> | null = null
let onSessionEnded: (() => void) | null = null

export function setAccessToken(token: string | null) {
  accessToken = token
}

/** Called when a refresh fails, so the app can return to the sign-in page. */
export function onSessionEnd(handler: () => void) {
  onSessionEnded = handler
}

export async function refreshSession(): Promise<boolean> {
  refreshing ??= (async () => {
    try {
      const res = await fetch(`${BASE}/auth/refresh`, { method: 'POST', credentials: 'same-origin' })
      if (!res.ok) {
        accessToken = null
        return false
      }
      const body = (await res.json()) as { accessToken: string }
      accessToken = body.accessToken
      return true
    } catch {
      return false
    } finally {
      // Let the next 401 start a new refresh.
      setTimeout(() => (refreshing = null), 0)
    }
  })()
  return refreshing
}

async function toError(res: Response): Promise<ApiError> {
  try {
    const problem = (await res.json()) as { title?: string; code?: string }
    return new ApiError(res.status, problem.code ?? `http_${res.status}`, problem.title ?? res.statusText)
  } catch {
    const fallback = res.status === 403 ? 'You do not have access to do this.' : 'Something went wrong. Try again.'
    return new ApiError(res.status, `http_${res.status}`, fallback)
  }
}

export async function api<T>(path: string, init: RequestInit = {}, retry = true): Promise<T> {
  const headers = new Headers(init.headers)
  // JSON bodies are strings; FormData sets its own multipart boundary.
  if (typeof init.body === 'string' && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)

  const res = await fetch(`${BASE}${path}`, { ...init, headers, credentials: 'same-origin' })

  if (res.status === 401 && retry && !path.startsWith('/auth/')) {
    if (await refreshSession()) return api<T>(path, init, false)
    onSessionEnded?.()
  }

  if (!res.ok) throw await toError(res)
  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

/**
 * Downloads a protected file. A plain link can't send the access token, so fetch it
 * and hand the browser a temporary object URL to save.
 */
export async function download(path: string, fileName: string, retry = true): Promise<void> {
  const headers = new Headers()
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
  const res = await fetch(`${BASE}${path}`, { headers, credentials: 'same-origin' })
  if (res.status === 401 && retry && (await refreshSession())) return download(path, fileName, false)
  if (!res.ok) throw await toError(res)

  const url = URL.createObjectURL(await res.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

/** Sends a JSON payload plus files as multipart/form-data. */
export function postForm<T>(path: string, payload: unknown, files: File[] = []): Promise<T> {
  const form = new FormData()
  form.append('payload', JSON.stringify(payload))
  files.forEach((file) => form.append('files', file))
  return api<T>(path, { method: 'POST', body: form })
}

export const get = <T>(path: string) => api<T>(path)
export const post = <T>(path: string, body?: unknown) =>
  api<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })
export const put = <T>(path: string, body: unknown) => api<T>(path, { method: 'PUT', body: JSON.stringify(body) })
export const del = <T = void>(path: string) => api<T>(path, { method: 'DELETE' })
