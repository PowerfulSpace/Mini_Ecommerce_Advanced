import { watch } from 'vue'
import { useAuthStore } from '@/stores/auth'

export interface UseUserScopedFetchOptions {
  immediate?: boolean
}

export function useUserScopedFetch(
  fetcher: () => Promise<void> | void,
  options: UseUserScopedFetchOptions = {},
): void {
  const authStore = useAuthStore()

  watch(
    () => authStore.identityVersion,
    async () => {
      try {
        await fetcher()
      } catch (error) {
        if (import.meta.env.DEV) {
          console.warn('[useUserScopedFetch] fetcher failed after identity change.', error)
        }
      }
    },
    { immediate: options.immediate ?? false },
  )
}
