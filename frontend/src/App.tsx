import AppBar from '@mui/material/AppBar'
import Box from '@mui/material/Box'
import Container from '@mui/material/Container'
import Tab from '@mui/material/Tab'
import Tabs from '@mui/material/Tabs'
import Toolbar from '@mui/material/Toolbar'
import Typography from '@mui/material/Typography'
import { Link, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import AlertsPage from './AlertsPage'
import MerchantsPage from './MerchantsPage'

export default function App() {
  const { pathname } = useLocation()
  const tab = pathname.startsWith('/obchody') ? '/obchody' : '/nalezy'
  return (
    <Box sx={{ minHeight: '100vh', bgcolor: '#f5f5f5' }}>
      <AppBar position="static" color="default" elevation={1}>
        <Toolbar sx={{ gap: 3 }}>
          <Typography variant="h6" sx={{ fontWeight: 700 }}>Kontrola webů obchodníků</Typography>
          <Tabs value={tab}>
            <Tab label="Nálezy" value="/nalezy" component={Link} to="/nalezy" />
            <Tab label="Obchody" value="/obchody" component={Link} to="/obchody" />
          </Tabs>
        </Toolbar>
      </AppBar>
      <Container maxWidth="xl" sx={{ py: 3 }}>
        <Routes>
          <Route path="/nalezy" element={<AlertsPage />} />
          <Route path="/obchody" element={<MerchantsPage />} />
          <Route path="*" element={<Navigate to="/nalezy" replace />} />
        </Routes>
      </Container>
    </Box>
  )
}
