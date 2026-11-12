import { useQuery } from '@tanstack/react-query'
import { api } from './api'

export interface Summary {
  openAlerts: number
  merchants: number
  found: number
  ok: number
  timeout: number
  unreachable: number
  unchecked: number
}

/** Counts for the sidebar badges and stat cards (cheap: pageSize=1 requests, only totals are used). */
export function useSummary() {
  return useQuery({
    queryKey: ['summary'],
    queryFn: async (): Promise<Summary> => {
      const total = (status?: string) => api.merchants({ status, page: 1, pageSize: 1 }).then((p) => p.total)
      const [openAlerts, merchants, found, ok, timeout, unreachable, unchecked] = await Promise.all([
        api.alerts({ resolved: false, page: 1, pageSize: 1 }).then((p) => p.total),
        total(), total('found'), total('ok'), total('timeout'), total('unreachable'), total('unchecked'),
      ])
      return { openAlerts, merchants, found, ok, timeout, unreachable, unchecked }
    },
  })
}
