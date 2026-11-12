import Chip from '@mui/material/Chip'
import Tooltip from '@mui/material/Tooltip'

/**
 * One chip for all states in the app. Colors:
 * red = found / unresolved, yellow = unfinished (timeout), orange = unreachable,
 * green = OK, grey = unchecked / resolved. Timeout and unreachable are never green.
 */
export type Status = 'found' | 'open' | 'timeout' | 'unreachable' | 'ok' | 'unchecked' | 'resolved' | 'found-resolved'

const STYLES: Record<Status, { label: string; bg: string; fg: string }> = {
  found: { label: 'Nalezeno', bg: '#d32f2f', fg: '#fff' },
  open: { label: 'Nevyřízeno', bg: '#d32f2f', fg: '#fff' },
  timeout: { label: 'Nedokončeno', bg: '#fbc02d', fg: '#000' },
  unreachable: { label: 'Nedostupné', bg: '#ef6c00', fg: '#fff' },
  ok: { label: 'OK', bg: '#2e7d32', fg: '#fff' },
  unchecked: { label: 'Nezkontrolováno', bg: '#9e9e9e', fg: '#fff' },
  resolved: { label: 'Vyřízeno', bg: '#9e9e9e', fg: '#fff' },
  'found-resolved': { label: 'Nalezeno, vyřízeno', bg: '#9e9e9e', fg: '#fff' },
}

export default function StatusChip({ status, label, title }: { status: Status; label?: string; title?: string | null }) {
  const s = STYLES[status]
  const chip = <Chip size="small" label={label ?? s.label} sx={{ bgcolor: s.bg, color: s.fg, fontWeight: 600 }} />
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
