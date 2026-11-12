import Box from '@mui/material/Box'
import Tooltip from '@mui/material/Tooltip'
import { STATUS_COLORS } from './theme'

/**
 * One chip for all states in the app. Colors:
 * red = found / unresolved, yellow = unfinished (timeout), orange = unreachable,
 * green = OK, grey = unchecked / resolved. Timeout and unreachable are never green.
 */
export type Status = 'found' | 'open' | 'timeout' | 'unreachable' | 'ok' | 'unchecked' | 'resolved' | 'found-resolved'

export const STATUS_STYLE: Record<Status, { label: string; color: keyof typeof STATUS_COLORS }> = {
  found: { label: 'Nalezeno', color: 'red' },
  open: { label: 'Nevyřízeno', color: 'red' },
  timeout: { label: 'Nedokončeno', color: 'yellow' },
  unreachable: { label: 'Nedostupné', color: 'orange' },
  ok: { label: 'OK', color: 'green' },
  unchecked: { label: 'Nezkontrolováno', color: 'grey' },
  resolved: { label: 'Vyřízeno', color: 'grey' },
  'found-resolved': { label: 'Nalezeno, vyřízeno', color: 'grey' },
}

export default function StatusChip({ status, label, title }: { status: Status; label?: string; title?: string | null }) {
  const s = STATUS_STYLE[status]
  const c = STATUS_COLORS[s.color]
  const chip = (
    <Box
      component="span"
      sx={{
        display: 'inline-flex', alignItems: 'center', gap: 0.75, px: 1.25, py: 0.25, borderRadius: 999,
        bgcolor: c.soft, color: c.text, border: `1px solid ${c.border}`, fontSize: 12.5, fontWeight: 600,
        lineHeight: '20px', whiteSpace: 'nowrap',
      }}
    >
      <Box component="span" sx={{ width: 7, height: 7, borderRadius: '50%', bgcolor: c.main, flexShrink: 0 }} />
      {label ?? s.label}
    </Box>
  )
  return title ? <Tooltip title={title}>{chip}</Tooltip> : chip
}

/** Status of a merchant's last check, taking unresolved findings into account. */
export function merchantStatus(outcome: string | null, openAlerts: number): Status {
  if (outcome === null) return 'unchecked'
  if (outcome === 'timeout') return 'timeout'
  if (outcome === 'unreachable') return 'unreachable'
  if (outcome === 'found') return openAlerts > 0 ? 'found' : 'found-resolved'
  return 'ok'
}
