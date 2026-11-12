import { useEffect, useRef, useState } from 'react'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import LinearProgress from '@mui/material/LinearProgress'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, api, type Run } from './api'
import { useToast } from './Toast'

/** "Spustit kontrolu": sends all merchants to POST /runs and polls GET /runs/{id} until it finishes. */
export default function RunPanel() {
  const qc = useQueryClient()
  const toast = useToast()
  const [run, setRun] = useState<{ id: string; total: number } | null>(null)

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
    qc.invalidateQueries({ queryKey: ['merchants'] })
    qc.invalidateQueries({ queryKey: ['alerts'] })
    qc.invalidateQueries({ queryKey: ['summary'] })
    if (data.status === 'done') toast('Kontrola dokončena')
    else toast('Kontrola skončila chybou', 'error')
  }, [data, qc, toast])

  // While running, refresh the merchant list as results come in.
  const doneCount = data?.results.length ?? 0
  useEffect(() => {
    if (data?.status === 'running' && doneCount > 0) qc.invalidateQueries({ queryKey: ['merchants'] })
  }, [doneCount, data?.status, qc])

  const running = start.isPending || data?.status === 'running' || (!!run && !data)
  const total = run?.total ?? 0

  return (
    <Paper sx={{ p: 2, mb: 2 }}>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
        <Button variant="contained" onClick={() => start.mutate()} disabled={running}>
          {running ? 'Kontrola běží…' : 'Spustit kontrolu'}
        </Button>
        <Typography variant="body2" color="text.secondary">
          Zkontroluje úvodní stránky všech obchodů v seznamu (nejvýše 10 s na web).
        </Typography>
      </Stack>

      {start.isError && <StartError error={start.error} />}

      {run && data && (
        <Box sx={{ mt: 2 }}>
          <Stack direction="row" sx={{ justifyContent: 'space-between', mb: 0.5 }}>
            <Typography variant="body2">
              {data.status === 'running' ? 'Probíhá kontrola' : data.status === 'done' ? 'Kontrola dokončena' : 'Kontrola selhala'}: {doneCount} z {total} obchodů
            </Typography>
            <Summary run={data} />
          </Stack>
          <LinearProgress variant="determinate" value={total ? (doneCount / total) * 100 : 0} color={data.status === 'failed' ? 'error' : 'primary'} />
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
  const errors = run.results.filter((r) => r.status === 'error').length
  const withMatches = run.results.filter((r) => r.matches.length > 0).length
  return (
    <Typography variant="body2" color="text.secondary">
      s nálezem: <b>{withMatches}</b> · chyba/nedokončeno: <b>{errors}</b> · OK bez nálezu: <b>{run.results.length - errors - withMatches}</b>
    </Typography>
  )
}
