import { hasValidSession } from './auth'

export type AuthMode = 'required' | 'optional'

const defaultAuthMode: AuthMode = 'required'

function normalizeAuthMode(rawMode: string | undefined): AuthMode {
  if (rawMode === 'optional') {
    return 'optional'
  }

  if (rawMode === 'required' || !rawMode) {
    return defaultAuthMode
  }

  console.warn(`[session-policy] invalid VITE_AUTH_MODE: ${rawMode}, fallback to ${defaultAuthMode}`)
  return defaultAuthMode
}

const authMode = normalizeAuthMode(import.meta.env.VITE_AUTH_MODE)

export function getAuthMode(): AuthMode {
  return authMode
}

export function isAuthRequired(): boolean {
  return authMode === 'required'
}

export function canAccessScan(): boolean {
  if (!isAuthRequired()) {
    return true
  }

  return hasValidSession()
}

export function shouldRedirectFromLogin(): boolean {
  return isAuthRequired() && hasValidSession()
}
