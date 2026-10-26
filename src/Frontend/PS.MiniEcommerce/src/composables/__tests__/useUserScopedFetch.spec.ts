import { beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope, nextTick } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { useUserScopedFetch } from '@/composables/useUserScopedFetch'
import { useAuthStore } from '@/stores/auth'

describe('useUserScopedFetch', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('does not call the fetcher on mount when immediate is false', () => {
    const fetcher = vi.fn()
    const scope = effectScope()

    scope.run(() => useUserScopedFetch(fetcher))

    expect(fetcher).not.toHaveBeenCalled()

    scope.stop()
  })

  it('calls the fetcher on mount when immediate is true', () => {
    const fetcher = vi.fn()
    const scope = effectScope()

    scope.run(() => useUserScopedFetch(fetcher, { immediate: true }))

    expect(fetcher).toHaveBeenCalledTimes(1)

    scope.stop()
  })

  it('calls the fetcher when identityVersion changes', async () => {
    const fetcher = vi.fn()
    const scope = effectScope()
    scope.run(() => useUserScopedFetch(fetcher))

    useAuthStore().setUser('alice')
    await nextTick()

    expect(fetcher).toHaveBeenCalledTimes(1)

    scope.stop()
  })

  it('swallows a rejected fetcher so the watcher keeps working', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetcher = vi
      .fn()
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce(undefined)
    const scope = effectScope()
    scope.run(() => useUserScopedFetch(fetcher))

    useAuthStore().setUser('alice')
    await nextTick()

    useAuthStore().setUser('bob')
    await nextTick()

    expect(fetcher).toHaveBeenCalledTimes(2)

    warn.mockRestore()
    scope.stop()
  })

  it('stops reacting after the scope is disposed', async () => {
    const fetcher = vi.fn()
    const scope = effectScope()
    scope.run(() => useUserScopedFetch(fetcher))

    scope.stop()

    useAuthStore().setUser('alice')
    await nextTick()

    expect(fetcher).not.toHaveBeenCalled()
  })
})
