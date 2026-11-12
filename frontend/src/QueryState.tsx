import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import { ApiError } from './api'

/** Error box with retry, used for every failed request. */
export function ErrorBox({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const message = error instanceof ApiError ? error.message : 'Nepodařilo se načíst data.'
  return (
    <Alert severity="error" action={onRetry && <Button color="inherit" size="small" onClick={onRetry}>Zkusit znovu</Button>}>
      {message}
    </Alert>
  )
}
