import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { useOrderDetailStore } from '@/stores/orderDetail'
import { cancelOrder, getOrder } from '@/services/api/orders'
import type { ApiError, OrderDetail } from '@/types/api'

vi.mock('@/services/api/orders', () => ({
  createOrder: vi.fn(),
  getOrders: vi.fn(),
  getOrder: vi.fn(),
  cancelOrder: vi.fn(),
}))

const getOrderMock = vi.mocked(getOrder)
const cancelOrderMock = vi.mocked(cancelOrder)

const order: OrderDetail = {
  id: 'order-1',
  status: 'Created',
  createdAtUtc: '2026-01-15T12:30:00Z',
  totalAmount: 19,
  items: [
    {
      productId: 'p1',
      productName: 'Keyboard',
      unitPrice: 9.5,
      quantity: 2,
      lineTotal: 19,
    },
  ],
}

describe('orderDetailStore', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    getOrderMock.mockReset()
    cancelOrderMock.mockReset()
  })

  it('fetch loads the order and marks it as loaded', async () => {
    getOrderMock.mockResolvedValue(order)
    const store = useOrderDetailStore()

    await store.fetch('order-1')

    expect(getOrderMock).toHaveBeenCalledWith('order-1')
    expect(store.order).toEqual(order)
    expect(store.notFound).toBe(false)
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
    expect(store.isLoaded).toBe(true)
  })

  it('treats order_not_found as a distinct not-found outcome', async () => {
    getOrderMock.mockRejectedValue({
      status: 404,
      code: 'order_not_found',
      raw: null,
    } as ApiError)
    const store = useOrderDetailStore()

    await store.fetch('missing')

    expect(store.order).toBeNull()
    expect(store.notFound).toBe(true)
    expect(store.error).toBeNull()
    expect(store.isLoaded).toBe(false)
  })

  it('stores other failures as an error', async () => {
    const apiError: ApiError = { status: 500, code: 'health_unavailable', raw: null }
    getOrderMock.mockRejectedValue(apiError)
    const store = useOrderDetailStore()

    await store.fetch('order-1')

    expect(store.order).toBeNull()
    expect(store.notFound).toBe(false)
    expect(store.error).toEqual(apiError)
  })

  it('replaces state between ids without caching', async () => {
    getOrderMock.mockResolvedValueOnce(order).mockResolvedValueOnce({
      ...order,
      id: 'order-2',
    })
    const store = useOrderDetailStore()

    await store.fetch('order-1')
    await store.fetch('order-2')

    expect(store.order?.id).toBe('order-2')
  })

  it('reset clears the state', async () => {
    getOrderMock.mockResolvedValue(order)
    const store = useOrderDetailStore()
    await store.fetch('order-1')
    store.cancelError = { status: 409, code: 'order_already_cancelled', raw: null }

    store.reset()

    expect(store.order).toBeNull()
    expect(store.loading).toBe(false)
    expect(store.error).toBeNull()
    expect(store.notFound).toBe(false)
    expect(store.cancelling).toBe(false)
    expect(store.cancelError).toBeNull()
    expect(store.isLoaded).toBe(false)
  })

  it('is reset when the development identity changes', async () => {
    getOrderMock.mockResolvedValue(order)
    const store = useOrderDetailStore()
    await store.fetch('order-1')
    expect(store.order).not.toBeNull()

    useAuthStore().setUser('bob')

    expect(store.order).toBeNull()
  })

  it('cancel replaces the order with the returned Cancelled order', async () => {
    const cancelled: OrderDetail = { ...order, status: 'Cancelled' }
    getOrderMock.mockResolvedValue(order)
    cancelOrderMock.mockResolvedValue(cancelled)
    const store = useOrderDetailStore()
    await store.fetch('order-1')

    await store.cancel()

    expect(cancelOrderMock).toHaveBeenCalledWith('order-1')
    expect(store.order?.status).toBe('Cancelled')
    expect(store.cancelError).toBeNull()
    expect(store.cancelling).toBe(false)
  })

  it('cancel is a no-op when the order is not Cancellable', async () => {
    const cancelled: OrderDetail = { ...order, status: 'Cancelled' }
    getOrderMock.mockResolvedValue(cancelled)
    const store = useOrderDetailStore()
    await store.fetch('order-1')

    await store.cancel()

    expect(cancelOrderMock).not.toHaveBeenCalled()
  })

  it('cancel is a no-op when no order is loaded', async () => {
    const store = useOrderDetailStore()

    await store.cancel()

    expect(cancelOrderMock).not.toHaveBeenCalled()
    expect(store.cancelError).toBeNull()
  })

  it('cancel surfaces order_already_cancelled and refreshes the order', async () => {
    const cancelled: OrderDetail = { ...order, status: 'Cancelled' }
    getOrderMock.mockResolvedValue(order)
    cancelOrderMock.mockRejectedValue({
      status: 409,
      code: 'order_already_cancelled',
      raw: null,
    } as ApiError)
    const store = useOrderDetailStore()
    await store.fetch('order-1')

    // The refetch performed during handling shows the persisted status.
    getOrderMock.mockResolvedValue(cancelled)

    await store.cancel()

    expect(store.cancelError?.code).toBe('order_already_cancelled')
    expect(store.order?.status).toBe('Cancelled')
  })

  it('cancel treats order_not_found as a not-found outcome', async () => {
    getOrderMock.mockResolvedValue(order)
    cancelOrderMock.mockRejectedValue({
      status: 404,
      code: 'order_not_found',
      raw: null,
    } as ApiError)
    const store = useOrderDetailStore()
    await store.fetch('order-1')

    await store.cancel()

    expect(store.cancelError?.code).toBe('order_not_found')
    expect(store.notFound).toBe(true)
    expect(store.order).toBeNull()
  })

  it('cancel keeps the order on a network failure', async () => {
    getOrderMock.mockResolvedValue(order)
    cancelOrderMock.mockRejectedValue({ status: 0, raw: null } as ApiError)
    const store = useOrderDetailStore()
    await store.fetch('order-1')

    await store.cancel()

    expect(store.cancelError).toBeTruthy()
    expect(store.order).toEqual(order)
    expect(store.cancelling).toBe(false)
  })

  it('canCancel reflects the order status and in-flight state', async () => {
    getOrderMock.mockResolvedValue(order)
    const store = useOrderDetailStore()
    expect(store.canCancel).toBe(false)

    await store.fetch('order-1')

    expect(store.canCancel).toBe(true)

    const cancelled: OrderDetail = { ...order, status: 'Cancelled' }
    store.order = cancelled
    expect(store.canCancel).toBe(false)
  })
})
