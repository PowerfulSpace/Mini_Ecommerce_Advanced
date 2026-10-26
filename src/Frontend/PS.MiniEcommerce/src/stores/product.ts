import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { getProduct } from '@/services/api/products'
import type { ApiError, Product } from '@/types/api'

export const useProductStore = defineStore('product', () => {
  const product = ref<Product | null>(null)
  const loading = ref(false)
  const error = ref<ApiError | null>(null)
  const notFound = ref(false)

  const isLoaded = computed(
    () => product.value !== null && !loading.value && error.value === null && !notFound.value,
  )

  async function fetch(id: string): Promise<void> {
    // No caching between ids: every call fully replaces the previous state.
    loading.value = true
    product.value = null
    error.value = null
    notFound.value = false

    try {
      product.value = await getProduct(id)
    } catch (caught) {
      // The response interceptor normalizes failures into ApiError. A missing or
      // inactive product is a distinct outcome from a generic failure.
      const apiError = caught as ApiError
      if (apiError.code === 'product_not_found') {
        notFound.value = true
      } else {
        error.value = apiError
      }
    } finally {
      loading.value = false
    }
  }

  function reset(): void {
    product.value = null
    loading.value = false
    error.value = null
    notFound.value = false
  }

  return { product, loading, error, notFound, isLoaded, fetch, reset }
})
