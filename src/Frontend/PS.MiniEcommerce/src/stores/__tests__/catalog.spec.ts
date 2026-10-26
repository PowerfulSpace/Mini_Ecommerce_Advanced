import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { useCatalogStore } from '@/stores/catalog'
import { getProducts } from '@/services/api/products'
import type { ApiError, Paginated, Product } from '@/types/api'

vi.mock('@/services/api/products', () => ({
  getProducts: vi.fn(),
  getProduct: vi.fn(),
}))

const getProductsMock = vi.mocked(getProducts)

const product: Product = {
  id: 'p1',
  name: 'Keyboard',
  description: 'Split keyboard',
  price: 9.5,
  stockQuantity: 50,
}

function page(
  items: Product[],
  pageNumber = 1,
  pageSize = 20,
  totalCount = items.length,
): Paginated<Product> {
  return { items, page: pageNumber, pageSize, totalCount }
}

describe('catalogStore', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    getProductsMock.mockReset()
  })

  it('fetchPage stores items, page and totalCount', async () => {
    getProductsMock.mockResolvedValue(page([product], 2, 20, 41))
    const store = useCatalogStore()

    await store.fetchPage(2)

    expect(getProductsMock).toHaveBeenCalledWith({ page: 2, pageSize: 20 })
    expect(store.items).toEqual([product])
    expect(store.page).toBe(2)
    expect(store.totalCount).toBe(41)
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('stores the error without throwing', async () => {
    const apiError: ApiError = { status: 500, code: 'health_unavailable', raw: null }
    getProductsMock.mockRejectedValue(apiError)
    const store = useCatalogStore()

    await expect(store.fetchPage(1)).resolves.toBeUndefined()

    expect(store.error).toEqual(apiError)
    expect(store.items).toEqual([])
    expect(store.loading).toBe(false)
  })

  it('computes totalPages and isEmpty', async () => {
    const store = useCatalogStore()
    expect(store.totalPages).toBe(1)
    expect(store.isEmpty).toBe(true)

    getProductsMock.mockResolvedValue(page([product], 1, 20, 41))
    await store.fetchPage(1)

    expect(store.totalPages).toBe(3)
    expect(store.isEmpty).toBe(false)
  })

  it('setPageSize clamps to the documented bounds and reloads the first page', async () => {
    getProductsMock.mockResolvedValue(page([product], 1, 100, 1))
    const store = useCatalogStore()

    await store.setPageSize(500)

    expect(store.pageSize).toBe(100)
    expect(store.page).toBe(1)
    expect(getProductsMock).toHaveBeenCalledWith({ page: 1, pageSize: 100 })
  })

  it('reset clears the state', async () => {
    getProductsMock.mockResolvedValue(page([product], 3, 20, 50))
    const store = useCatalogStore()
    await store.fetchPage(3)

    store.reset()

    expect(store.items).toEqual([])
    expect(store.page).toBe(1)
    expect(store.pageSize).toBe(20)
    expect(store.totalCount).toBe(0)
    expect(store.error).toBeNull()
    expect(store.isEmpty).toBe(true)
  })

  it('keeps its state when the development identity changes', async () => {
    getProductsMock.mockResolvedValue(page([product], 1, 20, 1))
    const store = useCatalogStore()
    await store.fetchPage(1)
    expect(store.items).toHaveLength(1)

    useAuthStore().setUser('alice')

    expect(store.items).toEqual([product])
    expect(store.totalCount).toBe(1)
  })
})
