import { useEffect, useRef, useState } from 'react'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import LinearProgress from '@mui/material/LinearProgress'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import PlayArrowRounded from '@mui/icons-material/PlayArrowRounded'
import TravelExploreOutlined from '@mui/icons-material/TravelExploreOutlined'
import { ApiError, api, type Run } from './api'
import StatusChip from './StatusChip'
import { useToast } from './Toast'

type RunRef = { id: string; total: number }
const STORAGE_KEY = 'gatewatch.activeRun'

// The running check survives a page reload; storage may be unavailable (private mode), so failures are ignored.
function loadRun(): RunRef | null {
  try {
    const v = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null')
    return v && typeof v.id === 'string' && typeof v.total === 'number' ? v : null
  } catch {
    return null
  }
}
function saveRun(r: RunRef | null) {
  try {
    if (r) localStorage.setItem(STORAGE_KEY, JSON.stringify(r))
    else localStorage.removeItem(STORAGE_KEY)
  } catch { /* ignore */ }
}

/** "Spustit kontrolu": sends all merchants to POST /runs and polls GET /runs/{id} until it finishes. */
export default function RunPanel() {
  const qc = useQueryClient()
  const toast = useToast()
  const [run, setRunState] = useState<RunRef | null>(loadRun)
  const setRun = (r: RunRef | null) => { saveRun(r); setRunState(r) }

  const start = useMutation({
    mutationFn: async () => {
      const merchants = await api.allMerchants()
      if (merchants.length === 0) throw new ApiError(400, 'V databázi nejsou žádné obchody ke kontrole.')
      const { run_id } = await api.startRun(merchants.map((m) => ({ id: m.id, url: m.url })))
      return { id: run_id, total: merchants.length }
    },
    onSuccess: (r) => setRun(r),
  })

  const status = useQuery({
    queryKey: ['run', run?.id],
    queryFn: () => api.run(run!.id),
    enabled: !!run,
    refetchInterval: (q) => (q.state.data?.status === 'running' || !q.state.data ? 2000 : false),
  })

  // Refresh lists once the run is finished.
  const finished = useRef<string | null>(null)
  const data = status.data
  useEffect(() => {
    if (!data || data.status === 'running' || finished.current === data.run_id) return
    finished.current = data.run_id
    saveRun(null) // finished: do not resume it after the next reload
    qc.invalidateQueries({ queryKey: ['merchants'] })
    qc.invalidateQueries({ queryKey: ['alerts'] })
    qc.invalidateQueries({ queryKey: ['summary'] })
    if (data.status === 'done') toast('Kontrola dokončena')
    else toast('Kontrola skončila chybou', 'error')
  }, [data, qc, toast])

  // While running, refresh the merchant list as results come in.
  const doneCount = data?.results.length ?? 0
  useEffect(() => {
    if (data?.status === 'running' && doneCount > 0) {
      qc.invalidateQueries({ queryKey: ['merchants'] })
      qc.invalidateQueries({ queryKey: ['summary'] })
    }
  }, [doneCount, data?.status, qc])

  // A remembered run that no longer exists (e.g. database reset) is forgotten instead of blocking the button.
  const statusError = status.error
  useEffect(() => {
    if (statusError instanceof ApiError && statusError.status === 404) { saveRun(null); setRunState(null) }
  }, [statusError])

  const running = start.isPending || data?.status === 'running' || (!!run && !data && !status.isError)
  const total = run?.total ?? 0

  return (
    <Paper variant="outlined" sx={{ p: 2.5, mb: 3 }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ alignItems: { sm: 'center' } }}>
        <Box sx={{ width: 44, height: 44, borderRadius: 2.5, bgcolor: '#eef2ff', color: 'primary.main', display: 'grid', placeItems: 'center', flexShrink: 0 }}>
          <TravelExploreOutlined />
        </Box>
        <Box sx={{ flex: 1 }}>
          <Typography variant="subtitle1" sx={{ fontWeight: 650 }}>Kontrola úvodních stránek</Typography>
          <Typography variant="body2" color="text.secondary">
            Zkontroluje všechny obchody v evidenci. Na web nejvýše 10 s, nic se neodesílá ani neproklikává.
          </Typography>
        </Box>
        <Button variant="contained" size="large" startIcon={running ? undefined : <PlayArrowRounded />} onClick={() => start.mutate()} disabled={running}>
          {running ? 'Kontrola běží…' : 'Spustit kontrolu'}
        </Button>
      </Stack>

      {start.isError && <StartError error={start.error} />}

      {run && data && (
        <Box sx={{ mt: 2.5 }}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={1} sx={{ justifyContent: 'space-between', mb: 1 }}>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              {data.status === 'running' ? 'Probíhá kontrola' : data.status === 'done' ? 'Kontrola dokončena' : 'Kontrola selhala'}: {doneCount} z {total} obchodů
            </Typography>
            <Summary run={data} />
          </Stack>
          <LinearProgress variant="determinate" value={total ? (doneCount / total) * 100 : 0} color={data.status === 'failed' ? 'error' : 'primary'} sx={{ height: 8, borderRadius: 999 }} />
          {data.status === 'failed' && (
            <Alert severity="error" sx={{ mt: 1 }}>
              {data.error} Výsledky dokončených obchodů zůstaly uložené, ostatní obchody zkontrolované nejsou.
            </Alert>
          )}
        </Box>
      )}
      {status.isError && <Alert severity="error" sx={{ mt: 1 }}>Nepodařilo se načíst průběh kontroly: {(status.error as Error).message}</Alert>}
    </Paper>
  )
}

function StartError({ error }: { error: unknown }) {
  const e = error as ApiError
  return (
    <Alert severity={e.status === 503 ? 'warning' : 'error'} sx={{ mt: 2 }}>
      {e.status === 503 ? `${e.message} Aplikace po startu načítá blacklist.` : e.message}
    </Alert>
  )
}

function Summary({ run }: { run: Run }) {
  // A timeout can also have matches: count it in both groups, but never as "OK bez nálezu".
  const withMatches = run.results.filter((r) => r.matches.length > 0).length
  const errors = run.results.filter((r) => r.status === 'error').length
  const clean = run.results.filter((r) => r.status === 'ok' && r.matches.length === 0).length
  return (
    <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 0.5 }}>
      <StatusChip status="found" label={`s nálezem ${withMatches}`} />
      <StatusChip status="timeout" label={`chyba/nedokončeno ${errors}`} />
      <StatusChip status="ok" label={`OK bez nálezu ${clean}`} />
    </Stack>
  )
}
