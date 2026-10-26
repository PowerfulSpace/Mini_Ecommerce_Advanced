import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'

const STORAGE_KEY = 'mini-ecommerce:test-user'

describe('authStore', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('starts unauthenticated when storage is empty', () => {
    const store = useAuthStore()

    expect(store.testUser).toBeNull()
    expect(store.isAuthenticated).toBe(false)
  })

  it('setUser stores the user in memory and in storage', () => {
    const store = useAuthStore()

    store.setUser('alice')

    expect(store.testUser).toBe('alice')
    expect(store.isAuthenticated).toBe(true)
    expect(localStorage.getItem(STORAGE_KEY)).toBe('alice')
  })

  it('reads a persisted user when the store is created', () => {
    localStorage.setItem(STORAGE_KEY, 'bob')

    const store = useAuthStore()

    expect(store.testUser).toBe('bob')
    expect(store.isAuthenticated).toBe(true)
  })

  it('ignores an unknown persisted value', () => {
    localStorage.setItem(STORAGE_KEY, 'mallory')

    const store = useAuthStore()

    expect(store.testUser).toBeNull()
  })

  it('clear removes the user from memory and storage', () => {
    const store = useAuthStore()
    store.setUser('alice')

    store.clear()

    expect(store.testUser).toBeNull()
    expect(store.isAuthenticated).toBe(false)
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull()
  })

  it('runs registered user-scoped resetters on setUser and clear', () => {
    const store = useAuthStore()
    let calls = 0
    store.registerUserScopedReset(() => {
      calls += 1
    })

    store.setUser('alice')
    store.clear()

    expect(calls).toBe(2)
  })

  it('increments identityVersion on setUser', () => {
    const store = useAuthStore()

    store.setUser('alice')

    expect(store.identityVersion).toBe(1)
  })

  it('increments identityVersion on clear', () => {
    const store = useAuthStore()

    store.clear()

    expect(store.identityVersion).toBe(1)
  })

  it('increments identityVersion even when the same user is selected again', () => {
    const store = useAuthStore()

    store.setUser('alice')
    store.setUser('alice')

    expect(store.identityVersion).toBe(2)
  })

  it('increments identityVersion before running resetters', () => {
    const store = useAuthStore()
    let versionDuringReset = -1
    store.registerUserScopedReset(() => {
      versionDuringReset = store.identityVersion
    })

    store.setUser('alice')

    expect(versionDuringReset).toBe(1)
  })

  it('unregisters a user-scoped resetter through the returned function', () => {
    const store = useAuthStore()
    let calls = 0
    const unregister = store.registerUserScopedReset(() => {
      calls += 1
    })

    unregister()
    store.setUser('alice')

    expect(calls).toBe(0)
  })
})
