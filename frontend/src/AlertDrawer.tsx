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
import { Link } from 'react-router-dom'
import { KIND_LABELS, api, formatDate, type AlertItem } from './api'
import StatusChip from './StatusChip'
import { useToast } from './Toast'

const KIND_HINTS: Record<string, string> = {
  script: 'Stránka tento skript skutečně načítala.',
  stylesheet: 'Stránka tento styl skutečně načítala.',
  image: 'Stránka tento obrázek skutečně načítala.',
  iframe: 'Stránka tuto adresu načítala ve vloženém rámu (iframe).',
  link: 'Na stránce je odkaz (a[href]) na tuto adresu. Odkaz se neotevíral.',
  form: 'Formulář na stránce odesílá data na tuto adresu (form[action]). Nic se neodesílalo.',
  redirect: 'Stránka nebo některý její požadavek na tuto adresu přesměrovával.',
  other: 'Stránka na tuto adresu poslala požadavek (např. XHR/fetch, font).',
}

const SOURCE_LABELS: Record<string, string> = {
  urlhaus: 'URLhaus (abuse.ch)',
  'urlhaus-snapshot': 'URLhaus – uložený snapshot',
  custom: 'Vlastní testovací záznam',
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary">{label}</Typography>
      <Box sx={{ wordBreak: 'break-all' }}>{children}</Box>
    </Box>
  )
}

/** Side panel: what was found and where, which blacklist entry it matched, and the resolve form. */
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

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(alert!.url)
      toast('Adresa zkopírována', 'info')
    } catch {
      toast('Kopírování se nepodařilo, označte adresu ručně.', 'warning')
    }
  }

  return (
    <Drawer anchor="right" open={!!alert} onClose={onClose}>
      {alert && (
        <Stack spacing={2} sx={{ width: { xs: '100vw', sm: 500 }, p: 3 }}>
          <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center' }}>
            <Typography variant="h6">Nález u obchodu {alert.merchant_id}</Typography>
            <StatusChip status={alert.resolved ? 'resolved' : 'open'} />
          </Stack>

          <Field label="Škodlivá adresa">
            <Typography sx={{ fontFamily: 'monospace', bgcolor: '#f5f5f5', p: 1, borderRadius: 1 }}>{alert.url}</Typography>
            <Button size="small" onClick={copy} sx={{ mt: 0.5 }}>Kopírovat adresu</Button>
          </Field>

          <Field label="Co se našlo a kde">
            <b>{KIND_LABELS[alert.kind] ?? alert.kind}</b> na úvodní stránce {alert.merchant_url}
            <Typography variant="body2" color="text.secondary">{KIND_HINTS[alert.kind]}</Typography>
          </Field>

          <Field label="Shoda s blacklistem">
            Přesná shoda se záznamem <span style={{ fontFamily: 'monospace' }}>{alert.url}</span>
            <Typography variant="body2" color="text.secondary">Zdroj: {SOURCE_LABELS[alert.blacklist_source] ?? alert.blacklist_source}</Typography>
          </Field>

          <Stack direction="row" spacing={4}>
            <Field label="Poprvé viděno">{formatDate(alert.first_seen_at)}</Field>
            <Field label="Naposledy viděno">{formatDate(alert.last_seen_at)}</Field>
          </Stack>

          <Link to={`/nalezy?merchant_id=${encodeURIComponent(alert.merchant_id)}`} onClick={onClose}>
            Všechny nálezy obchodu {alert.merchant_id}
          </Link>

          <Divider />

          {alert.resolved && (
            <Alert severity="info">
              Vyřízeno {formatDate(alert.resolved_at)}{alert.note ? <> s poznámkou: „{alert.note}“</> : ' bez poznámky.'}
              <br />Při dalších kontrolách zůstává vyřízený. Nová adresa u tohoto obchodu by byla nový nález.
            </Alert>
          )}

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
