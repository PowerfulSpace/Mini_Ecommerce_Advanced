import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { useCheckoutStore } from '@/stores/checkout'
import { useCartStore } from '@/stores/cart'
import { createOrder } from '@/services/api/orders'
import { getCart } from '@/services/api/cart'
import { getOrCreateKey, peekKey } from '@/utils/idempotency'
import type { ApiError, Cart, CartItem, OrderDetail } from '@/types/api'

vi.mock('@/services/api/orders', () => ({ createOrder: vi.fn() }))
vi.mock('@/services/api/cart', () => ({
  getCart: vi.fn(),
  addItem: vi.fn(),
  updateItem: vi.fn(),
  removeItem: vi.fn(),
}))

const createOrderMock = vi.mocked(createOrder)
const getCartMock = vi.mocked(getCart)

const item: CartItem = {
  productId: 'p1',
  productName: 'Keyboard',
  unitPrice: 9.5,
  quantity: 2,
  lineTotal: 19,
  isAvailable: true,
}

const emptyCart: Cart = { items: [], totalAmount: 0 }

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

function apiError(partial: Partial<ApiError>): ApiError {
  return { status: 0, raw: null, ...partial }
}

function seedCart(etag = '"V1"'): ReturnType<typeof useCartStore> {
  const store = useCartStore()
  store.items = [item]
  store.totalAmount = 19
  store.etag = etag
  return store
}

describe('checkoutStore', () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('a successful submit clears the key, resets the cart and stores the order id', async () => {
    createOrderMock.mockResolvedValue(order)
    const cartStore = seedCart()
    const checkout = useCheckoutStore()

    await checkout.submit()

    expect(createOrderMock).toHaveBeenCalledWith(expect.any(String), '"V1"')
    expect(checkout.lastOrderId).toBe('order-1')
    expect(peekKey()).toBeNull()
    expect(cartStore.items).toEqual([])
    expect(cartStore.etag).toBeNull()
    expect(checkout.error).toBeNull()
    expect(checkout.submitting).toBe(false)
  })

  it('503 idempotency_in_progress sets inProgress, keeps the key and keeps the cart', async () => {
    createOrderMock.mockRejectedValue(
      apiError({ status: 503, code: 'idempotency_in_progress' }),
    )
    const cartStore = seedCart()
    const checkout = useCheckoutStore()

    await checkout.submit()

    expect(checkout.inProgress).toBe(true)
    expect(checkout.error).toBeNull()
    expect(checkout.lastOrderId).toBeNull()
    expect(peekKey()).not.toBeNull()
    expect(cartStore.items).toEqual([item])
    expect(cartStore.etag).toBe('"V1"')
  })

  it('409 idempotency_key_reused sets the error and keeps the key', async () => {
    createOrderMock.mockRejectedValue(
      apiError({ status: 409, code: 'idempotency_key_reused' }),
    )
    seedCart()
    const checkout = useCheckoutStore()

    await checkout.submit()

    expect(checkout.error?.code).toBe('idempotency_key_reused')
    expect(peekKey()).not.toBeNull()
  })

  it('a final business error clears the key and surfaces the error', async () => {
    createOrderMock.mockRejectedValue(
      apiError({ status: 409, code: 'insufficient_stock', detail: 'Not enough stock' }),
    )
    seedCart()
    const checkout = useCheckoutStore()

    await checkout.submit()

    expect(checkout.error?.code).toBe('insufficient_stock')
    expect(peekKey()).toBeNull()
  })

  it('cart_version_conflict clears the key and refetches the cart', async () => {
    createOrderMock.mockRejectedValue(
      apiError({ status: 412, code: 'cart_version_conflict' }),
    )
    getCartMock.mockResolvedValue({ data: emptyCart, etag: '"V2"' })
    seedCart()
    const checkout = useCheckoutStore()

    await checkout.submit()

    expect(checkout.error?.code).toBe('cart_version_conflict')
    expect(peekKey()).toBeNull()
    expect(getCartMock).toHaveBeenCalledTimes(1)
  })

  it('a network error keeps the key so the same attempt can be retried', async () => {
    createOrderMock.mockRejectedValue(apiError({ status: 0 }))
    seedCart()
    const checkout = useCheckoutStore()

    await checkout.submit()

    expect(checkout.error).toBeTruthy()
    expect(peekKey()).not.toBeNull()
  })

  it('a retry after 503 reuses the same key and the same If-Match', async () => {
    createOrderMock
      .mockRejectedValueOnce(apiError({ status: 503, code: 'idempotency_in_progress' }))
      .mockResolvedValueOnce(order)
    seedCart()
    const checkout = useCheckoutStore()

    await checkout.submit()
    expect(checkout.inProgress).toBe(true)

    await checkout.submit()

    expect(createOrderMock).toHaveBeenCalledTimes(2)
    const [firstKey, firstEtag] = createOrderMock.mock.calls[0] ?? []
    const [secondKey, secondEtag] = createOrderMock.mock.calls[1] ?? []
    expect(secondKey).toBe(firstKey)
    expect(secondEtag).toBe(firstEtag)
    expect(checkout.lastOrderId).toBe('order-1')
    expect(peekKey()).toBeNull()
  })

  it('a retry after a network error reuses the same key and the same If-Match', async () => {
    createOrderMock
      .mockRejectedValueOnce(apiError({ status: 0 }))
      .mockResolvedValueOnce(order)
    seedCart()
    const checkout = useCheckoutStore()

    await checkout.submit()
    expect(checkout.error).toBeTruthy()

    await checkout.submit()

    expect(createOrderMock).toHaveBeenCalledTimes(2)
    const [firstKey, firstEtag] = createOrderMock.mock.calls[0] ?? []
    const [secondKey, secondEtag] = createOrderMock.mock.calls[1] ?? []
    expect(secondKey).toBe(firstKey)
    expect(secondEtag).toBe(firstEtag)
    expect(checkout.lastOrderId).toBe('order-1')
    expect(peekKey()).toBeNull()
  })

  it('is reset when the development identity changes without clearing the idempotency key', () => {
    const checkout = useCheckoutStore()
    checkout.error = apiError({ status: 500 })
    checkout.inProgress = true
    checkout.lastOrderId = 'order-1'
    const key = getOrCreateKey()

    useAuthStore().setUser('bob')

    expect(checkout.error).toBeNull()
    expect(checkout.inProgress).toBe(false)
    expect(checkout.lastOrderId).toBeNull()
    expect(peekKey()).toBe(key)
  })

  it('submit refuses to run without a cart ETag', async () => {
    const cartStore = useCartStore()
    cartStore.etag = null
    const checkout = useCheckoutStore()

    await expect(checkout.submit()).rejects.toThrow(/ETag/i)
    expect(createOrderMock).not.toHaveBeenCalled()
  })
})
