import Alert from '@mui/material/Alert'
import { ApiError } from '../api/http'

/** Shows the API's own message (they are written for end users) or a generic fallback. */
export function ErrorAlert({ error }: { error: unknown }) {
  if (!error) return null
  const message = error instanceof ApiError ? error.message : 'Something went wrong. Try again.'
  return <Alert severity="error">{message}</Alert>
}
