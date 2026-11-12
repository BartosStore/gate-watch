import type { ReactNode } from 'react'
import Box from '@mui/material/Box'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import ShieldOutlined from '@mui/icons-material/ShieldOutlined'
import ReportGmailerrorredOutlined from '@mui/icons-material/ReportGmailerrorredOutlined'
import StorefrontOutlined from '@mui/icons-material/StorefrontOutlined'
import { NavLink } from 'react-router-dom'
import { useSummary } from './useSummary'

function NavItem({ to, icon, label, badge }: { to: string; icon: ReactNode; label: string; badge?: number }) {
  return (
    <Box
      component={NavLink}
      to={to}
      sx={{
        display: 'flex', alignItems: 'center', gap: 1.5, px: 1.5, py: 1, borderRadius: 2, color: '#cbd5e1',
        textDecoration: 'none', fontWeight: 600, fontSize: 14, flexShrink: 0,
        '&:hover': { bgcolor: 'rgba(255,255,255,0.06)', color: '#fff' },
        '&.active': { bgcolor: 'rgba(129,140,248,0.18)', color: '#fff' },
      }}
    >
      {icon}
      <Box sx={{ flex: 1 }}>{label}</Box>
      {!!badge && (
        <Box sx={{ minWidth: 22, px: 0.75, borderRadius: 999, bgcolor: '#dc2626', color: '#fff', fontSize: 12, textAlign: 'center', lineHeight: '20px' }}>
          {badge}
        </Box>
      )}
    </Box>
  )
}

/** App shell: dark sidebar on desktop, compact top bar on narrow screens. */
export default function Layout({ children }: { children: ReactNode }) {
  const summary = useSummary().data
  return (
    <Box sx={{ display: 'flex', flexDirection: { xs: 'column', md: 'row' }, minHeight: '100vh', bgcolor: 'background.default' }}>
      <Box
        component="nav"
        sx={{
          width: { md: 248 }, flexShrink: 0, bgcolor: '#0f172a', color: '#fff', p: 2,
          display: 'flex', flexDirection: { xs: 'row', md: 'column' }, alignItems: { xs: 'center', md: 'stretch' }, gap: { xs: 1, md: 0.5 },
          position: { md: 'sticky' }, top: 0, height: { md: '100vh' },
        }}
      >
        <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center', px: 1, mb: { md: 3 }, mr: { xs: 1, md: 0 } }}>
          <Box sx={{ width: 34, height: 34, borderRadius: 2, display: 'grid', placeItems: 'center', background: 'linear-gradient(135deg,#6366f1,#8b5cf6)' }}>
            <ShieldOutlined fontSize="small" />
          </Box>
          <Box sx={{ display: { xs: 'none', sm: 'block' } }}>
            <Typography sx={{ fontWeight: 700, lineHeight: 1.1 }}>Gate Watch</Typography>
            <Typography sx={{ fontSize: 12, color: '#94a3b8' }}>Kontrola webů obchodníků</Typography>
          </Box>
        </Stack>
        <NavItem to="/nalezy" icon={<ReportGmailerrorredOutlined fontSize="small" />} label="Nálezy" badge={summary?.openAlerts} />
        <NavItem to="/obchody" icon={<StorefrontOutlined fontSize="small" />} label="Obchody" />
        <Box sx={{ flex: 1, display: { xs: 'none', md: 'block' } }} />
        {summary && (
          <Typography sx={{ display: { xs: 'none', md: 'block' }, px: 1, fontSize: 12, color: '#64748b' }}>
            {summary.merchants} obchodů v evidenci
          </Typography>
        )}
      </Box>
      <Box component="main" sx={{ flex: 1, minWidth: 0, p: { xs: 2, md: 4 } }}>
        <Box sx={{ maxWidth: 1400, mx: 'auto' }}>{children}</Box>
      </Box>
    </Box>
  )
}

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: ReactNode; actions?: ReactNode }) {
  return (
    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mb: 3, justifyContent: 'space-between', alignItems: { sm: 'flex-end' } }}>
      <Box>
        <Typography variant="h5">{title}</Typography>
        {subtitle && <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>{subtitle}</Typography>}
      </Box>
      {actions}
    </Stack>
  )
}
