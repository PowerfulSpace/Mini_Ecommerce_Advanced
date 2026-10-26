import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { getProducts } from '@/services/api/products'
import type { ApiError, Product } from '@/types/api'

// Documented pagination bounds (DECISIONS.md section 10).
export const DEFAULT_PAGE_SIZE = 20
export const MAX_PAGE_SIZE = 100

export const useCatalogStore = defineStore('catalog', () => {
  const items = ref<Product[]>([])
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
      const result = await getProducts({ page: requestedPage, pageSize: pageSize.value })
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

  // Changing the page size returns to the first page: the previous page number
  // may no longer exist with a different page size, and resetting the browsing
  // context is less surprising than landing on an empty or shifted page.
  async function setPageSize(size: number): Promise<void> {
    const clamped = Math.min(Math.max(Math.trunc(size), 1), MAX_PAGE_SIZE)
    if (clamped === pageSize.value) {
      return
    }

    pageSize.value = clamped
    await fetchPage(1)
  }

  function reset(): void {
    items.value = []
    page.value = 1
    pageSize.value = DEFAULT_PAGE_SIZE
    totalCount.value = 0
    loading.value = false
    error.value = null
  }

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
    setPageSize,
    reset,
  }
})
