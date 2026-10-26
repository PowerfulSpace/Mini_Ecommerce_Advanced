import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { useOrdersStore } from '@/stores/orders'
import { getOrders } from '@/services/api/orders'
import type { ApiError, OrderSummary, Paginated } from '@/types/api'

vi.mock('@/services/api/orders', () => ({
  createOrder: vi.fn(),
  getOrders: vi.fn(),
  getOrder: vi.fn(),
}))

const getOrdersMock = vi.mocked(getOrders)

const order: OrderSummary = {
  id: 'order-1',
  status: 'Created',
  createdAtUtc: '2026-01-15T12:30:00Z',
  totalAmount: 19,
}

function page(
  items: OrderSummary[],
  pageNumber = 1,
  pageSize = 20,
  totalCount = items.length,
): Paginated<OrderSummary> {
  return { items, page: pageNumber, pageSize, totalCount }
}

describe('ordersStore', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    getOrdersMock.mockReset()
  })

  it('fetchPage stores items, page and totalCount', async () => {
    getOrdersMock.mockResolvedValue(page([order], 2, 20, 41))
    const store = useOrdersStore()

    await store.fetchPage(2)

    expect(getOrdersMock).toHaveBeenCalledWith({ page: 2, pageSize: 20 })
    expect(store.items).toEqual([order])
    expect(store.page).toBe(2)
    expect(store.totalCount).toBe(41)
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('stores the error without throwing', async () => {
    const apiError: ApiError = { status: 500, code: 'health_unavailable', raw: null }
    getOrdersMock.mockRejectedValue(apiError)
    const store = useOrdersStore()

    await expect(store.fetchPage(1)).resolves.toBeUndefined()

    expect(store.error).toEqual(apiError)
    expect(store.items).toEqual([])
    expect(store.loading).toBe(false)
  })

  it('computes totalPages and isEmpty', async () => {
    const store = useOrdersStore()
    expect(store.totalPages).toBe(1)
    expect(store.isEmpty).toBe(true)

    getOrdersMock.mockResolvedValue(page([order], 1, 20, 41))
    await store.fetchPage(1)

    expect(store.totalPages).toBe(3)
    expect(store.isEmpty).toBe(false)
  })

  it('reset clears the state', async () => {
    getOrdersMock.mockResolvedValue(page([order], 3, 20, 50))
    const store = useOrdersStore()
    await store.fetchPage(3)

    store.reset()

    expect(store.items).toEqual([])
    expect(store.page).toBe(1)
    expect(store.pageSize).toBe(20)
    expect(store.totalCount).toBe(0)
    expect(store.error).toBeNull()
    expect(store.isEmpty).toBe(true)
  })

  it('is reset when the development identity changes', async () => {
    getOrdersMock.mockResolvedValue(page([order], 1, 20, 1))
    const store = useOrdersStore()
    await store.fetchPage(1)
    expect(store.items).toHaveLength(1)

    useAuthStore().setUser('alice')

    expect(store.items).toEqual([])
    expect(store.totalCount).toBe(0)
  })
})
