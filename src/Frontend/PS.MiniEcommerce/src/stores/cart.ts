import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import {
  addItem as addItemRequest,
  getCart,
  removeItem as removeItemRequest,
  updateItem as updateItemRequest,
  type CartResponse,
} from '@/services/api/cart'
import { useAuthStore } from '@/stores/auth'
import type { ApiError, CartItem } from '@/types/api'

function isVersionConflict(error: ApiError): boolean {
  return error.code === 'cart_version_conflict' || error.code === 'precondition_required'
}

export const useCartStore = defineStore('cart', () => {
  const items = ref<CartItem[]>([])
  const totalAmount = ref(0)
  const etag = ref<string | null>(null)
  const loading = ref(false)
  const mutating = ref(false)
  const error = ref<ApiError | null>(null)
  const conflict = ref(false)

  const itemCount = computed(() => items.value.reduce((sum, item) => sum + item.quantity, 0))
  const isEmpty = computed(() => items.value.length === 0)

  function apply(response: CartResponse): void {
    items.value = response.data.items
    totalAmount.value = response.data.totalAmount
    etag.value = response.etag
  }

  // Loads the authoritative cart without touching `conflict`; returns whether it
  // succeeded. `fetch()` and the conflict-recovery path build on this so that a
  // recovery load does not immediately hide the conflict warning.
  async function load(): Promise<boolean> {
    loading.value = true
    error.value = null

    try {
      apply(await getCart())
      return true
    } catch (caught) {
      // GET creates the cart lazily, so `cart_not_found` should not occur here.
      // Any failure is surfaced as a normal error.
      error.value = caught as ApiError
      return false
    } finally {
      loading.value = false
    }
  }

  async function fetch(): Promise<void> {
    if (await load()) {
      conflict.value = false
    }
  }

  // A failed recovery refetch must not replace the conflict warning with a
  // generic load error; the mutation itself is never replayed.
  async function recoverFromVersionConflict(): Promise<void> {
    try {
      await load()
    } catch (caught) {
      if (import.meta.env.DEV) {
        console.warn('[cart] Failed to refresh the cart after a version conflict.', caught)
      }
    } finally {
      error.value = null
      conflict.value = true
    }
  }

  async function runMutation(
    operation: (currentEtag: string) => Promise<CartResponse>,
  ): Promise<void> {
    mutating.value = true
    error.value = null

    try {
      if (etag.value === null && !(await load())) {
        return
      }

      const currentEtag = etag.value
      if (currentEtag === null) {
        return
      }

      try {
        apply(await operation(currentEtag))
        conflict.value = false
        return
      } catch (caught) {
        const apiError = caught as ApiError

        if (isVersionConflict(apiError)) {
          // A stale or missing precondition must never be replayed automatically.
          // Reload the authoritative cart, then flag the conflict for the user.
          await recoverFromVersionConflict()
          return
        }

        if (apiError.code === 'cart_not_found') {
          // The cart must exist before a mutation; fetch it, then retry once.
          if (!(await load())) {
            return
          }

          const refreshedEtag = etag.value
          if (refreshedEtag === null) {
            return
          }

          apply(await operation(refreshedEtag))
          conflict.value = false
          return
        }

        error.value = apiError
      }
    } catch (caught) {
      // Failures from the post-refetch retry (or an unexpected load failure).
      const apiError = caught as ApiError
      if (isVersionConflict(apiError)) {
        await recoverFromVersionConflict()
      } else {
        error.value = apiError
      }
    } finally {
      mutating.value = false
    }
  }

  function addItem(productId: string, quantity: number): Promise<void> {
    return runMutation((currentEtag) => addItemRequest(productId, quantity, currentEtag))
  }

  function updateItem(productId: string, quantity: number): Promise<void> {
    return runMutation((currentEtag) => updateItemRequest(productId, quantity, currentEtag))
  }

  function removeItem(productId: string): Promise<void> {
    return runMutation((currentEtag) => removeItemRequest(productId, currentEtag))
  }

  function reset(): void {
    items.value = []
    totalAmount.value = 0
    etag.value = null
    loading.value = false
    mutating.value = false
    error.value = null
    conflict.value = false
  }

  // Switching the development identity must drop the previous user's cart so the
  // next fetch loads the new user's cart instead of leaking Alice's items to Bob.
  useAuthStore().registerUserScopedReset(reset)

  return {
    items,
    totalAmount,
    etag,
    loading,
    mutating,
    error,
    conflict,
    itemCount,
    isEmpty,
    fetch,
    addItem,
    updateItem,
    removeItem,
    reset,
  }
})
