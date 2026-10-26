import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { createOrder } from '@/services/api/orders'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import { clearKey, getOrCreateKey } from '@/utils/idempotency'
import type { ApiError } from '@/types/api'

// Codes that represent a final decision the backend has persisted for the key.
// The key has done its job, so a fresh attempt needs a new key (DECISIONS.md
// section 13 D2 and api.md section 3.4).
const FINAL_BUSINESS_ERROR_CODES = new Set([
  'cart_empty',
  'insufficient_stock',
  'product_unavailable',
  'cart_not_found',
  'cart_version_conflict',
  'precondition_required',
])

export const useCheckoutStore = defineStore('checkout', () => {
  const submitting = ref(false)
  const error = ref<ApiError | null>(null)
  const inProgress = ref(false)
  const lastOrderId = ref<string | null>(null)

  // `inProgress` deliberately does not disable submission: after a 503 the user
  // must be able to retry the same attempt with the same key and If-Match.
  const canSubmit = computed(() => !submitting.value)

  async function submit(): Promise<void> {
    const cart = useCartStore()

    // Both the key and the ETag are read from their durable sources, never
    // regenerated here, so a retry reuses the exact same attempt identity.
    const key = getOrCreateKey()
    const etag = cart.etag

    if (etag === null) {
      throw new Error('Cannot submit checkout without a cart ETag.')
    }

    submitting.value = true
    error.value = null
    inProgress.value = false

    try {
      const order = await createOrder(key, etag)

      lastOrderId.value = order.id
      // The key succeeded; a future checkout is a genuinely new attempt.
      clearKey()
      // The backend cleared the cart items as part of the same transaction.
      cart.reset()
    } catch (caught) {
      const apiError = caught as ApiError

      if (apiError.code === 'idempotency_in_progress') {
        // Not a failure: the attempt is unresolved. Keep the key and the cart so
        // the user can retry with the same key and If-Match.
        inProgress.value = true
      } else if (apiError.code === 'idempotency_key_reused') {
        // Frontend-logic fault: the same key was sent with a different If-Match.
        // The key cannot be safely cleared automatically.
        error.value = apiError
        if (import.meta.env.DEV) {
          console.warn(
            '[checkout] The idempotency key was reused with a different If-Match. ' +
              'The key is retained; a new key is required only after a confirmed final result.',
          )
        }
      } else if (apiError.code !== undefined && FINAL_BUSINESS_ERROR_CODES.has(apiError.code)) {
        // The backend persisted this final error for the key, so the key is spent.
        clearKey()
        error.value = apiError

        if (
          apiError.code === 'cart_version_conflict' ||
          apiError.code === 'precondition_required'
        ) {
          await cart.fetch()
        }
      } else {
        // validation_error, a network failure, or a 5xx without a code: the
        // outcome is unknown, so the key is kept and the user may retry.
        error.value = apiError
      }
    } finally {
      submitting.value = false
    }
  }

  function reset(): void {
    submitting.value = false
    error.value = null
    inProgress.value = false
    lastOrderId.value = null
  }

  useAuthStore().registerUserScopedReset(reset)

  function clearOutstandingKey(): void {
    clearKey()
  }

  return {
    submitting,
    error,
    inProgress,
    lastOrderId,
    canSubmit,
    submit,
    reset,
    clearOutstandingKey,
  }
})
