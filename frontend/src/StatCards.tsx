import type { ReactNode } from 'react'
import Box from '@mui/material/Box'
import Card from '@mui/material/Card'
import CardActionArea from '@mui/material/CardActionArea'
import Skeleton from '@mui/material/Skeleton'
import Typography from '@mui/material/Typography'
import { STATUS_COLORS } from './theme'

export interface Stat {
  key: string
  label: string
  value: number | undefined
  hint?: ReactNode
  color: keyof typeof STATUS_COLORS
  onClick?: () => void
  active?: boolean
}

/** Row of KPI cards; the accent color follows the status color rules. */
export default function StatCards({ stats }: { stats: Stat[] }) {
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, 1fr)', md: `repeat(${stats.length}, 1fr)` }, gap: 2, mb: 3 }}>
      {stats.map((s) => {
        const c = STATUS_COLORS[s.color]
        const body = (
          <Box sx={{ p: 2, borderTop: `3px solid ${c.main}`, height: '100%' }}>
            <Typography variant="overline" color="text.secondary" sx={{ lineHeight: 1.4, display: 'block' }}>{s.label}</Typography>
            {s.value === undefined
              ? <Skeleton width={48} height={40} />
              : <Typography sx={{ fontSize: 30, fontWeight: 700, color: s.value > 0 && s.color !== 'grey' ? c.text : 'text.primary', lineHeight: 1.3 }}>{s.value}</Typography>}
            {s.hint && <Typography variant="caption" color="text.secondary">{s.hint}</Typography>}
          </Box>
        )
        return (
          <Card key={s.key} sx={{ borderColor: s.active ? c.main : undefined, boxShadow: s.active ? `0 0 0 1px ${c.main}` : undefined }}>
            {s.onClick ? <CardActionArea onClick={s.onClick} sx={{ height: '100%' }}>{body}</CardActionArea> : body}
          </Card>
        )
      })}
    </Box>
  )
}
