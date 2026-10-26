import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

export type TestUser = 'alice' | 'bob'

// Development-only identity storage. localStorage is used (not sessionStorage)
// so the selected identity survives reloads and browser restarts, which keeps
// the dev workflow stable and matches the "persist across reload" requirement.
const STORAGE_KEY = 'mini-ecommerce:test-user'

function readStoredUser(): TestUser | null {
  const value = localStorage.getItem(STORAGE_KEY)
  // Unknown values must never authenticate; only the known test identities count.
  return value === 'alice' || value === 'bob' ? value : null
}

export const useAuthStore = defineStore('auth', () => {
  const testUser = ref<TestUser | null>(readStoredUser())
  const identityVersion = ref(0)

  const isAuthenticated = computed(() => testUser.value !== null)

  // Hook for user-scoped stores (e.g. the future cartStore) to clear themselves
  // when the identity changes. Phase 10 will register real resetters here.
  const userScopedResetters: Array<() => void> = []

  function registerUserScopedReset(resetter: () => void): () => void {
    userScopedResetters.push(resetter)
    return () => {
      const index = userScopedResetters.indexOf(resetter)
      if (index !== -1) {
        userScopedResetters.splice(index, 1)
      }
    }
  }

  function resetUserScopedState(): void {
    for (const reset of userScopedResetters) {
      reset()
    }
  }

  function persist(): void {
    if (testUser.value === null) {
      localStorage.removeItem(STORAGE_KEY)
      return
    }

    localStorage.setItem(STORAGE_KEY, testUser.value)
  }

  function setUser(user: TestUser): void {
    testUser.value = user
    identityVersion.value += 1
    persist()
    resetUserScopedState()
  }

  function clear(): void {
    testUser.value = null
    identityVersion.value += 1
    persist()
    resetUserScopedState()
  }

  return {
    testUser,
    identityVersion,
    isAuthenticated,
    setUser,
    clear,
    registerUserScopedReset,
    resetUserScopedState,
  }
})
