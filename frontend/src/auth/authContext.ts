import { createContext, useContext } from 'react'
import type { GrantScope, MeResponse } from '../api/types'

export type AuthStatus = 'loading' | 'needs-setup' | 'anonymous' | 'authenticated'

export interface AuthState {
  status: AuthStatus
  me: MeResponse | null
  permissions: Record<string, GrantScope[]>
  login: (userNameOrEmail: string, password: string) => Promise<void>
  logout: () => Promise<void>
  /** True when the permission is granted anywhere. Per-record checks are done by the server. */
  can: (permissionKey: string) => boolean
  reload: () => Promise<void>
}

export const AuthContext = createContext<AuthState | null>(null)

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
