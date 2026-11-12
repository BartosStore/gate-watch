// Typed client for the backend API (see openapi.yaml). All responses are snake_case JSON.

export type CheckOutcome = 'ok' | 'found' | 'timeout' | 'unreachable'

export interface Paged<T> {
  items: T[]
  total: number
  page: number
  page_size: number
}

export interface AlertItem {
  id: string
  merchant_id: string
  merchant_url: string
  url: string
  kind: string
  blacklist_source: string
  first_seen_at: string
  last_seen_at: string
  resolved: boolean
  resolved_at: string | null
  note: string
  first_check_id: string
  last_check_id: string
}

export interface MerchantItem {
  id: string
  url: string
  last_outcome: CheckOutcome | null
  last_error: string | null
  last_duration_ms: number | null
  last_check_at: string | null
  open_alerts: number
}

export interface MerchantResult {
  merchant_id: string
  status: 'ok' | 'error'
  matches: string[]
  alerts: { id: string; url: string; resolved: boolean; note: string }[]
  error: string | null
}

export interface Run {
  run_id: string
  status: 'running' | 'done' | 'failed'
  results: MerchantResult[]
  error: string | null
}

export class ApiError extends Error {
  readonly status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response
  try {
    res = await fetch(path, {
      ...init,
      headers: init?.body ? { 'Content-Type': 'application/json; charset=utf-8' } : undefined,
    })
  } catch {
    throw new ApiError(0, 'Server neodpovídá. Zkontrolujte, že aplikace běží.')
  }
  const body = await res.json().catch(() => null)
  if (!res.ok) throw new ApiError(res.status, body?.error ?? `Chyba serveru (HTTP ${res.status})`)
  return body as T
}

const qs = (params: Record<string, string | number | boolean | undefined | null>) => {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') p.set(k, String(v))
  return p.toString()
}

export const api = {
  alerts: (p: { resolved?: boolean; merchant_id?: string; page: number; pageSize: number }) =>
    request<Paged<AlertItem>>(`/alerts?${qs(p)}`),
  merchants: (p: { search?: string; status?: string; page: number; pageSize: number }) =>
    request<Paged<MerchantItem>>(`/merchants?${qs(p)}`),
  run: (id: string) => request<Run>(`/runs/${encodeURIComponent(id)}`),
  startRun: (merchants: { id: string; url: string }[]) =>
    request<{ run_id: string }>('/runs', { method: 'POST', body: JSON.stringify({ merchants }) }),
  resolve: (id: string, note: string) =>
    request<{ ok: true }>(`/alerts/${encodeURIComponent(id)}/resolve`, { method: 'POST', body: JSON.stringify({ note }) }),

  /** All merchants from the DB (pages of 1000), used as input of "Spustit kontrolu". */
  async allMerchants(): Promise<MerchantItem[]> {
    const all: MerchantItem[] = []
    for (let page = 1; ; page++) {
      const p = await api.merchants({ page, pageSize: 1000 })
      all.push(...p.items)
      if (all.length >= p.total || p.items.length === 0) return all
    }
  },
}

export const formatDate = (iso: string | null) => (iso ? new Date(iso).toLocaleString('cs-CZ') : '–')

export const KIND_LABELS: Record<string, string> = {
  script: 'Skript',
  stylesheet: 'Styl (CSS)',
  image: 'Obrázek',
  iframe: 'Iframe',
  link: 'Odkaz',
  form: 'Formulář',
  redirect: 'Přesměrování',
  other: 'Jiný požadavek',
}

/** Czech plural: plural(1, 'nález', 'nálezy', 'nálezů') = '1 nález', 2–4 → nálezy, 0 and 5+ → nálezů. */
export const plural = (n: number, one: string, few: string, many: string) =>
  `${n} ${n === 1 ? one : n >= 2 && n <= 4 ? few : many}`
