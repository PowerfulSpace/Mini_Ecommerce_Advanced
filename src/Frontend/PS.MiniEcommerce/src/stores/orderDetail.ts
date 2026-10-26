import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { cancelOrder, getOrder } from '@/services/api/orders'
import { useAuthStore } from '@/stores/auth'
import type { ApiError, OrderDetail } from '@/types/api'

export const useOrderDetailStore = defineStore('orderDetail', () => {
  const order = ref<OrderDetail | null>(null)
  const loading = ref(false)
  const error = ref<ApiError | null>(null)
  const notFound = ref(false)
  const cancelling = ref(false)
  const cancelError = ref<ApiError | null>(null)

  const isLoaded = computed(
    () => order.value !== null && !loading.value && error.value === null && !notFound.value,
  )

  const canCancel = computed(
    () =>
      order.value !== null &&
      order.value.status === 'Created' &&
      !cancelling.value &&
      !loading.value,
  )

  async function fetch(id: string): Promise<void> {
    // No caching between ids: every call fully replaces the previous state, so a
    // previously opened order can never be shown while another id is loading.
    loading.value = true
    order.value = null
    error.value = null
    notFound.value = false
    cancelError.value = null

    try {
      order.value = await getOrder(id)
    } catch (caught) {
      // The response interceptor normalizes failures into ApiError. A missing or
      // not-owned order is a distinct outcome from a generic failure.
      const apiError = caught as ApiError
      if (apiError.code === 'order_not_found') {
        notFound.value = true
      } else {
        error.value = apiError
      }
    } finally {
      loading.value = false
    }
  }

  async function cancel(): Promise<void> {
    const current = order.value

    // Cancellation is only meaningful for a loaded, still-created order.
    if (current === null || current.status !== 'Created') {
      if (import.meta.env.DEV) {
        console.warn('[orderDetail] cancel() called for a non-cancellable order; ignoring.')
      }
      return
    }

    cancelling.value = true
    cancelError.value = null

    try {
      // The backend returns the updated order; no extra fetch is needed.
      order.value = await cancelOrder(current.id)
    } catch (caught) {
      const apiError = caught as ApiError

      if (apiError.code === 'order_not_found') {
        // The order disappeared or is no longer accessible.
        order.value = null
        notFound.value = true
        cancelError.value = apiError
      } else if (apiError.code === 'order_already_cancelled') {
        // Another tab or request won the race. Refetch so the UI shows the
        // persisted status instead of trusting the stale local copy.
        await fetch(current.id)
        cancelError.value = apiError
      } else {
        // Network failure or unexpected 5xx: keep the loaded order as-is.
        cancelError.value = apiError
      }
    } finally {
      cancelling.value = false
    }
  }

  function reset(): void {
    order.value = null
    loading.value = false
    error.value = null
    notFound.value = false
    cancelling.value = false
    cancelError.value = null
  }

  // Order details are user-scoped; switching identity must clear them.
  useAuthStore().registerUserScopedReset(reset)

  return {
    order,
    loading,
    error,
    notFound,
    cancelling,
    cancelError,
    isLoaded,
    canCancel,
    fetch,
    cancel,
    reset,
  }
})
