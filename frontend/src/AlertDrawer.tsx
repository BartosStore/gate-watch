import { useEffect, useState } from 'react'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Divider from '@mui/material/Divider'
import Drawer from '@mui/material/Drawer'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { KIND_LABELS, api, formatDate, type AlertItem } from './api'
import StatusChip from './StatusChip'
import { useToast } from './Toast'

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary">{label}</Typography>
      <Box sx={{ wordBreak: 'break-all' }}>{children}</Box>
    </Box>
  )
}

/** Side panel with what was found, where, and the resolve form. */
export default function AlertDrawer({ alert, onClose }: { alert: AlertItem | null; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [note, setNote] = useState('')
  useEffect(() => setNote(alert?.note ?? ''), [alert])

  const resolve = useMutation({
    mutationFn: () => api.resolve(alert!.id, note),
    onSuccess: () => {
      toast(alert!.resolved ? 'Poznámka uložena' : 'Nález vyřízen')
      qc.invalidateQueries({ queryKey: ['alerts'] })
      qc.invalidateQueries({ queryKey: ['merchants'] })
      qc.invalidateQueries({ queryKey: ['run'] })
      onClose()
    },
  })

  return (
    <Drawer anchor="right" open={!!alert} onClose={onClose}>
      {alert && (
        <Stack spacing={2} sx={{ width: 480, p: 3 }}>
          <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center' }}>
            <Typography variant="h6">Nález</Typography>
            <StatusChip status={alert.resolved ? 'resolved' : 'open'} />
          </Stack>

          <Field label="Škodlivá adresa (shoda s blacklistem)"><Typography sx={{ fontFamily: 'monospace' }}>{alert.url}</Typography></Field>
          <Field label="Kde se našla">{KIND_LABELS[alert.kind] ?? alert.kind}</Field>
          <Field label="Obchod">{alert.merchant_id} – {alert.merchant_url}</Field>
          <Field label="Zdroj blacklistu">{alert.blacklist_source}</Field>
          <Stack direction="row" spacing={4}>
            <Field label="Poprvé viděno">{formatDate(alert.first_seen_at)}</Field>
            <Field label="Naposledy viděno">{formatDate(alert.last_seen_at)}</Field>
          </Stack>

          <Divider />

          <Typography variant="subtitle1">{alert.resolved ? 'Upravit poznámku' : 'Vyřídit nález'}</Typography>
          <TextField label="Poznámka (nepovinná)" multiline minRows={3} value={note} onChange={(e) => setNote(e.target.value)} slotProps={{ htmlInput: { maxLength: 10000 } }} />
          {resolve.isError && <Alert severity="error">{(resolve.error as Error).message}</Alert>}
          <Stack direction="row" spacing={1}>
            <Button variant="contained" onClick={() => resolve.mutate()} disabled={resolve.isPending}>
              {resolve.isPending ? 'Ukládám…' : alert.resolved ? 'Uložit poznámku' : 'Vyřídit'}
            </Button>
            <Button onClick={onClose}>Zavřít</Button>
          </Stack>
        </Stack>
      )}
    </Drawer>
  )
}
