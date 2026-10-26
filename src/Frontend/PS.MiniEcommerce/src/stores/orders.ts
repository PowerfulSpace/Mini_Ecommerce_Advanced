import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { getOrders } from '@/services/api/orders'
import { useAuthStore } from '@/stores/auth'
import type { ApiError, OrderSummary } from '@/types/api'

// Documented pagination bounds (DECISIONS.md section 10).
export const DEFAULT_PAGE_SIZE = 20

export const useOrdersStore = defineStore('orders', () => {
  const items = ref<OrderSummary[]>([])
  const page = ref(1)
  const pageSize = ref(DEFAULT_PAGE_SIZE)
  const totalCount = ref(0)
  const loading = ref(false)
  const error = ref<ApiError | null>(null)

  const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize.value)))
  const isEmpty = computed(() => items.value.length === 0 && !loading.value && !error.value)

  async function fetchPage(target: number): Promise<void> {
    const requestedPage = Number.isInteger(target) && target >= 1 ? target : 1

    loading.value = true
    error.value = null

    try {
      const result = await getOrders({ page: requestedPage, pageSize: pageSize.value })
      items.value = result.items
      page.value = result.page
      pageSize.value = result.pageSize
      totalCount.value = result.totalCount
    } catch (caught) {
      // fetchPage never throws; the failure is surfaced through `error`.
      error.value = caught as ApiError
      items.value = []
      totalCount.value = 0
    } finally {
      loading.value = false
    }
  }

  function reset(): void {
    items.value = []
    page.value = 1
    pageSize.value = DEFAULT_PAGE_SIZE
    totalCount.value = 0
    loading.value = false
    error.value = null
  }

  // Orders are scoped to the authenticated user, so switching identity must drop
  // the previous user's history instead of leaking it to the new identity.
  useAuthStore().registerUserScopedReset(reset)

  return {
    items,
    page,
    pageSize,
    totalCount,
    loading,
    error,
    totalPages,
    isEmpty,
    fetchPage,
    reset,
  }
})
