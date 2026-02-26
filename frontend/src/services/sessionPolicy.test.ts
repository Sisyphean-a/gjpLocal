import { afterEach, describe, expect, it, vi } from 'vitest'

const tomorrowIso = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString()

describe('sessionPolicy', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
    localStorage.clear()
  })

  it('requires session when auth mode is required', async () => {
    vi.stubEnv('VITE_AUTH_MODE', 'required')
    const { canAccessScan, isAuthRequired } = await import('./sessionPolicy')
    const { saveSession } = await import('./auth')

    expect(isAuthRequired()).toBe(true)
    expect(canAccessScan()).toBe(false)

    saveSession('token', tomorrowIso, 'user01')
    expect(canAccessScan()).toBe(true)
  })

  it('allows access without session when auth mode is optional', async () => {
    vi.stubEnv('VITE_AUTH_MODE', 'optional')
    const { canAccessScan, isAuthRequired } = await import('./sessionPolicy')

    expect(isAuthRequired()).toBe(false)
    expect(canAccessScan()).toBe(true)
  })
})
