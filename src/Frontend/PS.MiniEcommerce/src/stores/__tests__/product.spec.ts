import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { useProductStore } from '@/stores/product'
import { getProduct } from '@/services/api/products'
import type { ApiError, Product } from '@/types/api'

vi.mock('@/services/api/products', () => ({
  getProducts: vi.fn(),
  getProduct: vi.fn(),
}))

const getProductMock = vi.mocked(getProduct)

const product: Product = {
  id: 'p1',
  name: 'Keyboard',
  description: 'Split keyboard',
  price: 9.5,
  stockQuantity: 50,
}

describe('productStore', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    getProductMock.mockReset()
  })

  it('fetch fills the product and marks it loaded', async () => {
    getProductMock.mockResolvedValue(product)
    const store = useProductStore()

    await store.fetch('p1')

    expect(getProductMock).toHaveBeenCalledWith('p1')
    expect(store.product).toEqual(product)
    expect(store.notFound).toBe(false)
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(store.isLoaded).toBe(true)
  })

  it('treats product_not_found as notFound, not as an error', async () => {
    getProductMock.mockRejectedValue({
      status: 404,
      code: 'product_not_found',
      raw: null,
    } as ApiError)
    const store = useProductStore()

    await store.fetch('missing')

    expect(store.notFound).toBe(true)
    expect(store.error).toBeNull()
    expect(store.product).toBeNull()
    expect(store.isLoaded).toBe(false)
  })

  it('stores other failures as error and not notFound', async () => {
    const apiError: ApiError = { status: 500, detail: 'boom', raw: null }
    getProductMock.mockRejectedValue(apiError)
    const store = useProductStore()

    await store.fetch('p1')

    expect(store.error).toEqual(apiError)
    expect(store.notFound).toBe(false)
    expect(store.product).toBeNull()
    expect(store.isLoaded).toBe(false)
  })

  it('replaces state when the id changes instead of caching', async () => {
    getProductMock.mockResolvedValueOnce(product)
    getProductMock.mockResolvedValueOnce({ ...product, id: 'p2', name: 'Mouse' })
    const store = useProductStore()

    await store.fetch('p1')
    await store.fetch('p2')

    expect(store.product?.id).toBe('p2')
    expect(store.product?.name).toBe('Mouse')
  })

  it('reset clears the state', async () => {
    getProductMock.mockResolvedValue(product)
    const store = useProductStore()
    await store.fetch('p1')

    store.reset()

    expect(store.product).toBeNull()
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
    expect(store.notFound).toBe(false)
    expect(store.isLoaded).toBe(false)
  })

  it('keeps its state when the development identity changes', async () => {
    getProductMock.mockResolvedValue(product)
    const store = useProductStore()
    await store.fetch('p1')
    expect(store.product).not.toBeNull()

    useAuthStore().setUser('bob')

    expect(store.product).toEqual(product)
  })
})
