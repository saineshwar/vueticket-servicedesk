import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../../auth/authContext'
import { ErrorAlert } from '../../components/ErrorAlert'
import { AuthCard } from './AuthCard'

export function LoginPage() {
  const { t } = useTranslation()
  const { login } = useAuth()

  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [missing, setMissing] = useState(false)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (!userName.trim() || !password) {
      setMissing(true)
      return
    }
    setMissing(false)
    setBusy(true)
    try {
      await login(userName.trim(), password)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthCard title={t('login.title')}>
      <Stack component="form" spacing={2} onSubmit={submit} noValidate>
        {missing && <Alert severity="warning">{t('login.required')}</Alert>}
        <ErrorAlert error={error} />
        <TextField
          id="login-username"
          label={t('login.userName')}
          autoComplete="username"
          autoFocus
          value={userName}
          onChange={(e) => setUserName(e.target.value)}
        />
        <TextField
          id="login-password"
          label={t('login.password')}
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
        <Button type="submit" variant="contained" size="large" disabled={busy}>
          {t('login.submit')}
        </Button>
      </Stack>
    </AuthCard>
  )
}
