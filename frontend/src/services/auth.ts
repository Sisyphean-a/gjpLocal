const tokenStorageKey = 'swcs_token'
const tokenExpireKey = 'swcs_token_expire'
const usernameStorageKey = 'swcs_username'

export function saveSession(token: string, expiresAtUtc: string, username: string): void {
  localStorage.setItem(tokenStorageKey, token)
  localStorage.setItem(tokenExpireKey, expiresAtUtc)
  localStorage.setItem(usernameStorageKey, username)
}

export function clearSession(): void {
  localStorage.removeItem(tokenStorageKey)
  localStorage.removeItem(tokenExpireKey)
  localStorage.removeItem(usernameStorageKey)
}

export function getAccessToken(): string | null {
  return localStorage.getItem(tokenStorageKey)
}

export function getCurrentUsername(): string {
  return localStorage.getItem(usernameStorageKey) ?? ''
}

export function hasValidSession(): boolean {
  const token = getAccessToken()
  const expiresAt = localStorage.getItem(tokenExpireKey)
  if (!token || !expiresAt) {
    return false
  }

  const expireDate = new Date(expiresAt)
  if (Number.isNaN(expireDate.getTime())) {
    return false
  }

  return expireDate.getTime() > Date.now()
}
