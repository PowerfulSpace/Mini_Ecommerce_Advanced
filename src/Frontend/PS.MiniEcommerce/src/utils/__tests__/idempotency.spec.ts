import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearKey, getOrCreateKey, KEY_STORAGE_NAME, peekKey } from '@/utils/idempotency'

// Documented key format (DECISIONS.md section 10).
const KEY_PATTERN = /^[A-Za-z0-9_-]{8,128}$/

describe('idempotency', () => {
  beforeEach(() => {
    sessionStorage.clear()
    clearKey()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    sessionStorage.clear()
  })

  it('getOrCreateKey creates a key and persists it', () => {
    const key = getOrCreateKey()

    expect(sessionStorage.getItem(KEY_STORAGE_NAME)).toBe(key)
    expect(peekKey()).toBe(key)
  })

  it('returns the same key on a repeated call', () => {
    const first = getOrCreateKey()
    const second = getOrCreateKey()

    expect(second).toBe(first)
  })

  it('creates a key that matches the documented format', () => {
    expect(getOrCreateKey()).toMatch(KEY_PATTERN)
  })

  it('clearKey removes the key so the next call creates a new one', () => {
    const first = getOrCreateKey()

    clearKey()

    expect(peekKey()).toBeNull()
    expect(sessionStorage.getItem(KEY_STORAGE_NAME)).toBeNull()

    const second = getOrCreateKey()
    expect(second).not.toBe(first)
    expect(second).toMatch(KEY_PATTERN)
  })

  it('peekKey never creates a key', () => {
    expect(peekKey()).toBeNull()
    expect(sessionStorage.getItem(KEY_STORAGE_NAME)).toBeNull()
  })

  it('degrades to in-memory storage when sessionStorage is unavailable', () => {
    vi.stubGlobal('sessionStorage', {
      getItem: () => null,
      setItem: () => {
        throw new Error('storage disabled')
      },
      removeItem: () => {},
    })

    // Clear any value left by earlier tests so the fallback store starts empty.
    clearKey()
    expect(peekKey()).toBeNull()

    const first = getOrCreateKey()
    const second = getOrCreateKey()

    expect(first).toMatch(KEY_PATTERN)
    expect(second).toBe(first)

    clearKey()
    expect(peekKey()).toBeNull()
  })
})
