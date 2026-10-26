import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import { addItem, getCart, removeItem, updateItem } from '@/services/api/cart'
import type { ApiError, Cart, CartItem } from '@/types/api'

vi.mock('@/services/api/cart', () => ({
  getCart: vi.fn(),
  addItem: vi.fn(),
  updateItem: vi.fn(),
  removeItem: vi.fn(),
}))

const getCartMock = vi.mocked(getCart)
const addItemMock = vi.mocked(addItem)
const updateItemMock = vi.mocked(updateItem)
const removeItemMock = vi.mocked(removeItem)

const item: CartItem = {
  productId: 'p1',
  productName: 'Keyboard',
  unitPrice: 9.5,
  quantity: 2,
  lineTotal: 19,
  isAvailable: true,
}

const cart: Cart = { items: [item], totalAmount: 19 }
const emptyCart: Cart = { items: [], totalAmount: 0 }

describe('cartStore', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('fetch stores items, totalAmount and etag and clears conflict', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    const store = useCartStore()
    store.conflict = true

    await store.fetch()

    expect(store.items).toEqual([item])
    expect(store.totalAmount).toBe(19)
    expect(store.etag).toBe('"V1"')
    expect(store.conflict).toBe(false)
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
  })

  it('addItem fetches the cart first when there is no etag', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    addItemMock.mockResolvedValue({ data: { items: [item], totalAmount: 28.5 }, etag: '"V2"' })
    const store = useCartStore()

    await store.addItem('p1', 1)

    expect(getCartMock).toHaveBeenCalledTimes(1)
    expect(addItemMock).toHaveBeenCalledWith('p1', 1, '"V1"')
    expect(store.etag).toBe('"V2"')
    expect(store.totalAmount).toBe(28.5)
    expect(store.error).toBeNull()
  })

  it('a successful mutation updates items, totalAmount and etag', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    updateItemMock.mockResolvedValue({
      data: { items: [{ ...item, quantity: 5, lineTotal: 47.5 }], totalAmount: 47.5 },
      etag: '"V2"',
    })
    const store = useCartStore()
    await store.fetch()

    await store.updateItem('p1', 5)

    expect(store.items[0]?.quantity).toBe(5)
    expect(store.totalAmount).toBe(47.5)
    expect(store.etag).toBe('"V2"')
  })

  it('412 marks conflict, refetches and does not repeat the mutation', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V2"' })
    addItemMock.mockRejectedValue({
      status: 412,
      code: 'cart_version_conflict',
      raw: null,
    } as ApiError)
    const store = useCartStore()
    store.etag = '"V1"'

    await store.addItem('p1', 1)

    expect(store.conflict).toBe(true)
    expect(store.error).toBeNull()
    expect(addItemMock).toHaveBeenCalledTimes(1)
    expect(getCartMock).toHaveBeenCalledTimes(1)
    expect(store.etag).toBe('"V2"')
  })

  it('428 behaves like 412', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V2"' })
    updateItemMock.mockRejectedValue({
      status: 428,
      code: 'precondition_required',
      raw: null,
    } as ApiError)
    const store = useCartStore()
    store.etag = '"V1"'

    await store.updateItem('p1', 3)

    expect(store.conflict).toBe(true)
    expect(store.error).toBeNull()
    expect(updateItemMock).toHaveBeenCalledTimes(1)
    expect(getCartMock).toHaveBeenCalledTimes(1)
  })

  it('412 keeps the conflict warning when the recovery refetch fails', async () => {
    getCartMock.mockRejectedValue({
      status: 0,
      detail: 'Сеть недоступна',
      raw: null,
    } as ApiError)
    updateItemMock.mockRejectedValue({
      status: 412,
      code: 'cart_version_conflict',
      raw: null,
    } as ApiError)
    const store = useCartStore()
    store.etag = '"V1"'

    await store.updateItem('p1', 3)

    expect(store.conflict).toBe(true)
    expect(store.error).toBeNull()
    expect(updateItemMock).toHaveBeenCalledTimes(1)
    expect(getCartMock).toHaveBeenCalledTimes(1)
    expect(store.etag).toBe('"V1"')
  })

  it('insufficient_stock sets error and keeps the items unchanged', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    updateItemMock.mockRejectedValue({
      status: 409,
      code: 'insufficient_stock',
      detail: 'Not enough stock',
      raw: null,
    } as ApiError)
    const store = useCartStore()
    await store.fetch()

    await store.updateItem('p1', 999)

    expect(store.error?.code).toBe('insufficient_stock')
    expect(store.conflict).toBe(false)
    expect(store.items).toEqual([item])
    expect(updateItemMock).toHaveBeenCalledTimes(1)
  })

  it('cart_not_found on a mutation triggers exactly one retry through fetch', async () => {
    getCartMock
      .mockResolvedValueOnce({ data: cart, etag: '"V1"' })
      .mockResolvedValueOnce({ data: cart, etag: '"V2"' })
    addItemMock
      .mockRejectedValueOnce({ status: 404, code: 'cart_not_found', raw: null } as ApiError)
      .mockResolvedValueOnce({ data: { items: [item], totalAmount: 28.5 }, etag: '"V3"' })
    const store = useCartStore()

    await store.addItem('p1', 1)

    expect(addItemMock).toHaveBeenCalledTimes(2)
    expect(addItemMock).toHaveBeenLastCalledWith('p1', 1, '"V2"')
    expect(getCartMock).toHaveBeenCalledTimes(2)
    expect(store.error).toBeNull()
    expect(store.etag).toBe('"V3"')
  })

  it('a second cart_not_found failure surfaces as an error', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    addItemMock.mockRejectedValue({ status: 404, code: 'cart_not_found', raw: null } as ApiError)
    const store = useCartStore()
    store.etag = '"V0"'

    await store.addItem('p1', 1)

    expect(addItemMock).toHaveBeenCalledTimes(2)
    expect(store.error?.code).toBe('cart_not_found')
  })

  it('removeItem uses the dedicated delete operation', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    removeItemMock.mockResolvedValue({ data: emptyCart, etag: '"V2"' })
    const store = useCartStore()
    await store.fetch()

    await store.removeItem('p1')

    expect(removeItemMock).toHaveBeenCalledWith('p1', '"V1"')
    expect(store.items).toEqual([])
    expect(store.itemCount).toBe(0)
    expect(store.isEmpty).toBe(true)
  })

  it('reset clears the state', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    const store = useCartStore()
    await store.fetch()

    store.reset()

    expect(store.items).toEqual([])
    expect(store.totalAmount).toBe(0)
    expect(store.etag).toBeNull()
    expect(store.error).toBeNull()
    expect(store.conflict).toBe(false)
  })

  it('is reset when the development identity changes', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    const store = useCartStore()
    await store.fetch()
    expect(store.items).toHaveLength(1)

    useAuthStore().setUser('bob')

    expect(store.items).toEqual([])
    expect(store.etag).toBeNull()
  })
})
