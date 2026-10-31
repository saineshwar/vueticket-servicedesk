import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { authApi, meApi, setupApi } from '../api/endpoints'
import { onSessionEnd, refreshSession, setAccessToken } from '../api/http'
import type { GrantScope, MeResponse } from '../api/types'
import { AuthContext, type AuthState, type AuthStatus } from './authContext'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [me, setMe] = useState<MeResponse | null>(null)
  const [permissions, setPermissions] = useState<Record<string, GrantScope[]>>({})

  const loadProfile = useCallback(async () => {
    const [profile, perms] = await Promise.all([meApi.me(), meApi.permissions()])
    setMe(profile)
    setPermissions(perms.permissions)
    setStatus('authenticated')
  }, [])

  const signedOut = useCallback(() => {
    setAccessToken(null)
    setMe(null)
    setPermissions({})
    setStatus('anonymous')
  }, [])

  /** Works out where the user stands: setup needed, signed out, or signed in (via the refresh cookie). */
  const bootstrap = useCallback(async () => {
    try {
      const setup = await setupApi.status()
      if (!setup.isInitialized) {
        setStatus('needs-setup')
        return
      }
      // A page reload loses the in-memory token; the refresh cookie gets a new one.
      if (await refreshSession()) await loadProfile()
      else signedOut()
    } catch {
      signedOut()
    }
  }, [loadProfile, signedOut])

  const reload = useCallback(async () => {
    setStatus('loading')
    await bootstrap()
  }, [bootstrap])

  useEffect(() => {
    onSessionEnd(signedOut)
    // bootstrap() only sets state after awaiting the API, which is what effects are for.
    // oxlint-disable-next-line react/set-state-in-effect
    void bootstrap()
  }, [bootstrap, signedOut])

  const login = useCallback(
    async (userNameOrEmail: string, password: string) => {
      const tokens = await authApi.login(userNameOrEmail, password)
      setAccessToken(tokens.accessToken)
      await loadProfile()
    },
    [loadProfile],
  )

  const logout = useCallback(async () => {
    try {
      await authApi.logout()
    } finally {
      signedOut()
    }
  }, [signedOut])

  const value = useMemo<AuthState>(
    () => ({
      status,
      me,
      permissions,
      login,
      logout,
      reload,
      can: (key) => (permissions[key]?.length ?? 0) > 0,
    }),
    [status, me, permissions, login, logout, reload],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
