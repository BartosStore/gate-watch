import { Navigate, Route, Routes } from 'react-router-dom'
import AlertsPage from './AlertsPage'
import Layout from './Layout'
import MerchantsPage from './MerchantsPage'

export default function App() {
  return (
    <Layout>
      <Routes>
        <Route path="/nalezy" element={<AlertsPage />} />
        <Route path="/obchody" element={<MerchantsPage />} />
        <Route path="*" element={<Navigate to="/nalezy" replace />} />
      </Routes>
    </Layout>
  )
}
