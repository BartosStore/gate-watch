import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'
import Alert from '@mui/material/Alert'
import Snackbar from '@mui/material/Snackbar'

type Severity = 'success' | 'error' | 'info' | 'warning'
const ToastContext = createContext<(message: string, severity?: Severity) => void>(() => {})

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<{ message: string; severity: Severity; key: number } | null>(null)
  const show = useCallback((message: string, severity: Severity = 'success') => setToast({ message, severity, key: Date.now() }), [])
  return (
    <ToastContext.Provider value={show}>
      {children}
      <Snackbar key={toast?.key} open={!!toast} autoHideDuration={4000} onClose={() => setToast(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
        <Alert severity={toast?.severity} variant="filled" onClose={() => setToast(null)}>{toast?.message}</Alert>
      </Snackbar>
    </ToastContext.Provider>
  )
}

export const useToast = () => useContext(ToastContext)
