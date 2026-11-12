import { useEffect, useState, type ReactNode } from 'react'
import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Drawer from '@mui/material/Drawer'
import IconButton from '@mui/material/IconButton'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Tooltip from '@mui/material/Tooltip'
import Typography from '@mui/material/Typography'
import CloseRounded from '@mui/icons-material/CloseRounded'
import ContentCopyRounded from '@mui/icons-material/ContentCopyRounded'
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

const mono = 'ui-monospace, "Cascadia Mono", Consolas, monospace'

function Section({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Box>
      <Typography variant="overline" color="text.secondary">{label}</Typography>
      <Box sx={{ mt: 0.25 }}>{children}</Box>
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
      qc.invalidateQueries({ queryKey: ['summary'] })
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
    <Drawer anchor="right" open={!!alert} onClose={onClose} slotProps={{ paper: { sx: { width: { xs: '100vw', sm: 520 } } } }}>
      {alert && (
        <Stack sx={{ height: '100%' }}>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', px: 3, py: 2, borderBottom: 1, borderColor: 'divider' }}>
            <Box sx={{ flex: 1, minWidth: 0 }}>
              <Typography variant="overline" color="text.secondary">Nález</Typography>
              <Typography variant="h6" noWrap>{alert.merchant_id}</Typography>
            </Box>
            <StatusChip status={alert.resolved ? 'resolved' : 'open'} />
            <IconButton onClick={onClose} aria-label="Zavřít"><CloseRounded /></IconButton>
          </Stack>

          <Stack spacing={2.5} sx={{ p: 3, flex: 1, overflowY: 'auto' }}>
            <Section label="Škodlivá adresa">
              <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-start', bgcolor: '#0f172a', color: '#e2e8f0', borderRadius: 2, p: 1.5 }}>
                <Typography sx={{ fontFamily: mono, fontSize: 13, wordBreak: 'break-all', flex: 1 }}>{alert.url}</Typography>
                <Tooltip title="Kopírovat adresu">
                  <IconButton size="small" onClick={copy} sx={{ color: '#cbd5e1' }} aria-label="Kopírovat adresu"><ContentCopyRounded fontSize="small" /></IconButton>
                </Tooltip>
              </Stack>
            </Section>

            <Section label="Co se našlo a kde">
              <Typography variant="body2"><b>{KIND_LABELS[alert.kind] ?? alert.kind}</b> na úvodní stránce <span style={{ wordBreak: 'break-all' }}>{alert.merchant_url}</span></Typography>
              <Typography variant="body2" color="text.secondary">{KIND_HINTS[alert.kind]}</Typography>
            </Section>

            <Section label="Shoda s blacklistem">
              <Typography variant="body2">Přesná shoda se záznamem <span style={{ fontFamily: mono, wordBreak: 'break-all' }}>{alert.url}</span></Typography>
              <Typography variant="body2" color="text.secondary">Zdroj: {SOURCE_LABELS[alert.blacklist_source] ?? alert.blacklist_source}</Typography>
            </Section>

            <Stack direction="row" spacing={4}>
              <Section label="Poprvé viděno"><Typography variant="body2">{formatDate(alert.first_seen_at)}</Typography></Section>
              <Section label="Naposledy viděno"><Typography variant="body2">{formatDate(alert.last_seen_at)}</Typography></Section>
            </Stack>

            <Link to={`/nalezy?merchant_id=${encodeURIComponent(alert.merchant_id)}`} onClick={onClose}>
              Všechny nálezy obchodu {alert.merchant_id}
            </Link>

            {alert.resolved && (
              <Alert severity="info" variant="outlined">
                Vyřízeno {formatDate(alert.resolved_at)}{alert.note ? <> s poznámkou: „{alert.note}“</> : ' bez poznámky.'}
                <br />Při dalších kontrolách zůstává vyřízený. Nová adresa u tohoto obchodu by byla nový nález.
              </Alert>
            )}
          </Stack>

          <Stack spacing={1.5} sx={{ p: 3, borderTop: 1, borderColor: 'divider', bgcolor: '#f8fafc' }}>
            <Typography variant="subtitle2">{alert.resolved ? 'Upravit poznámku' : 'Vyřídit nález'}</Typography>
            <TextField
              placeholder="Poznámka (nepovinná), např. „ověřeno s obchodem, skript odstraněn“"
              multiline minRows={3} value={note} onChange={(e) => setNote(e.target.value)}
              slotProps={{ htmlInput: { maxLength: 10000 } }} sx={{ bgcolor: '#fff' }}
            />
            {resolve.isError && <Alert severity="error">{(resolve.error as Error).message}</Alert>}
            <Stack direction="row" spacing={1}>
              <Button variant="contained" onClick={() => resolve.mutate()} disabled={resolve.isPending}>
                {resolve.isPending ? 'Ukládám…' : alert.resolved ? 'Uložit poznámku' : 'Vyřídit'}
              </Button>
              <Button onClick={onClose}>Zavřít</Button>
            </Stack>
          </Stack>
        </Stack>
      )}
    </Drawer>
  )
}
