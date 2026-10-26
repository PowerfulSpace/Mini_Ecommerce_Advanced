import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { NButton, NInputNumber } from 'naive-ui'
import CartView from '@/views/CartView.vue'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import { useCheckoutStore } from '@/stores/checkout'
import { getCart, updateItem } from '@/services/api/cart'
import type { ApiError, Cart, CartItem } from '@/types/api'

vi.mock('@/services/api/cart', () => ({
  getCart: vi.fn(),
  addItem: vi.fn(),
  updateItem: vi.fn(),
  removeItem: vi.fn(),
}))

const { pushMock } = vi.hoisted(() => ({ pushMock: vi.fn() }))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {} }),
  useRouter: () => ({ push: pushMock }),
}))

const getCartMock = vi.mocked(getCart)
const updateItemMock = vi.mocked(updateItem)

function checkoutButton(wrapper: ReturnType<typeof mountView>) {
  return wrapper.findAllComponents(NButton).find((button) => button.text().includes('Оформить'))
}

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

function cartWithQuantity(quantity: number): Cart {
  return {
    items: [{ ...item, quantity, lineTotal: item.unitPrice * quantity }],
    totalAmount: item.unitPrice * quantity,
  }
}

function mountView() {
  return mount(CartView, {
    global: { stubs: { RouterLink: RouterLinkStub } },
  })
}

describe('CartView', () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    setActivePinia(createPinia())
    getCartMock.mockReset()
    updateItemMock.mockReset()
    pushMock.mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('renders the empty state with a link to the catalog', async () => {
    getCartMock.mockResolvedValue({ data: emptyCart, etag: '"V1"' })

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Корзина пуста')
    const link = wrapper.findAllComponents(RouterLinkStub).find((l) => l.props('to') === '/')
    expect(link).toBeTruthy()
  })

  it('renders the cart items, line totals and the overall total', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Keyboard')
    expect(wrapper.text()).toContain('$9.50')
    expect(wrapper.text()).toContain('$19.00')
    expect(wrapper.text()).toContain('Итого: $19.00')
    expect(wrapper.text()).toContain('Удалить')
  })

  it('marks unavailable items', async () => {
    getCartMock.mockResolvedValue({
      data: { items: [{ ...item, isAvailable: false }], totalAmount: 19 },
      etag: '"V1"',
    })

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Недоступен')
  })

  it('shows the error state with a refresh action', async () => {
    getCartMock.mockRejectedValue({ status: 500, detail: 'Сервер недоступен', raw: null } as ApiError)

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Сервер недоступен')
    expect(wrapper.text()).toContain('Обновить')
  })

  it('shows the conflict warning when the store reports a version conflict', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })

    const wrapper = mountView()
    await flushPromises()
    useCartStore().conflict = true
    await flushPromises()

    expect(wrapper.text()).toContain('Изменение не применено')
  })

  it('prefers the conflict warning over a cart error', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })

    const wrapper = mountView()
    await flushPromises()

    const store = useCartStore()
    store.error = {
      status: 500,
      detail: 'Не удалось загрузить корзину',
      raw: null,
    } as ApiError
    store.conflict = true
    await flushPromises()

    expect(wrapper.text()).toContain('Изменение не применено')
    expect(wrapper.text()).not.toContain('Не удалось загрузить корзину')
  })

  it('sets loading while the cart request is in flight', async () => {
    getCartMock.mockReturnValue(new Promise(() => {}))

    mountView()

    expect(useCartStore().loading).toBe(true)
  })

  it('disables the checkout button when the cart is empty', async () => {
    getCartMock.mockResolvedValue({ data: emptyCart, etag: '"V1"' })

    const wrapper = mountView()
    await flushPromises()

    expect(checkoutButton(wrapper)?.props('disabled')).toBe(true)
  })

  it('calls checkout.submit when the checkout button is clicked', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    const submitSpy = vi.spyOn(useCheckoutStore(), 'submit').mockResolvedValue()

    const wrapper = mountView()
    await flushPromises()

    await checkoutButton(wrapper)?.trigger('click')

    expect(submitSpy).toHaveBeenCalledTimes(1)
  })

  it('shows an error alert for a failed checkout', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })

    const wrapper = mountView()
    await flushPromises()

    const checkout = useCheckoutStore()
    checkout.error = {
      status: 409,
      code: 'insufficient_stock',
      detail: 'Недостаточно товара',
      raw: null,
    }
    await flushPromises()

    expect(wrapper.text()).toContain('Недостаточно товара')
  })

  it('shows an info alert while checkout is in progress', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })

    const wrapper = mountView()
    await flushPromises()

    useCheckoutStore().inProgress = true
    await flushPromises()

    expect(wrapper.text()).toContain('Заказ обрабатывается')
  })

  it('refetches the cart when the development identity changes', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })

    mountView()
    await flushPromises()
    expect(getCartMock).toHaveBeenCalledTimes(1)

    useAuthStore().setUser('bob')
    await flushPromises()

    expect(getCartMock).toHaveBeenCalledTimes(2)
  })

  it('recalculates the line total and the total amount from the draft immediately', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })

    const wrapper = mountView()
    await flushPromises()

    wrapper.findComponent(NInputNumber).vm.$emit('update:value', 3)
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.cart-item__line-total').text()).toBe('$28.50')
    expect(wrapper.find('.cart__total').text()).toBe('Итого: $28.50')
  })

  it('syncs the draft to the refetched cart after a 412 conflict', async () => {
    const serverCart: Cart = {
      items: [{ ...item, quantity: 4, lineTotal: 38 }],
      totalAmount: 38,
    }
    getCartMock
      .mockResolvedValueOnce({ data: cart, etag: '"V1"' })
      .mockResolvedValueOnce({ data: serverCart, etag: '"V2"' })
    updateItemMock.mockRejectedValue({
      status: 412,
      code: 'cart_version_conflict',
      raw: null,
    } as ApiError)

    const wrapper = mountView()
    await flushPromises()
    const input = wrapper.findComponent(NInputNumber)

    input.vm.$emit('update:value', 7)
    await wrapper.vm.$nextTick()
    input.vm.$emit('blur')
    await flushPromises()

    expect(updateItemMock).toHaveBeenCalledWith('p1', 7, '"V1"')
    expect(getCartMock).toHaveBeenCalledTimes(2)
    expect(input.props('value')).toBe(4)
    expect(wrapper.find('.cart-item__line-total').text()).toBe('$38.00')
    expect(wrapper.text()).toContain('Изменение не применено')
  })

  it('resyncs a stale draft when the conflict flag is set', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    const serverCart: Cart = {
      items: [{ ...item, quantity: 4, lineTotal: 38 }],
      totalAmount: 38,
    }
    getCartMock.mockResolvedValue({ data: serverCart, etag: '"V1"' })

    const wrapper = mountView()
    await flushPromises()
    const input = wrapper.findComponent(NInputNumber)

    input.vm.$emit('update:value', 7)
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.cart-item__line-total').text()).toBe('$66.50')

    useCartStore().conflict = true
    await flushPromises()

    expect(input.props('value')).toBe(4)
    expect(wrapper.find('.cart-item__line-total').text()).toBe('$38.00')
  })

  it('debounces quantity changes and sends one PUT after 500ms', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    updateItemMock.mockResolvedValue({ data: cartWithQuantity(4), etag: '"V2"' })

    const wrapper = mountView()
    await flushPromises()
    const input = wrapper.findComponent(NInputNumber)

    input.vm.$emit('update:value', 3)
    await wrapper.vm.$nextTick()
    await vi.advanceTimersByTimeAsync(200)

    input.vm.$emit('update:value', 4)
    await wrapper.vm.$nextTick()
    await vi.advanceTimersByTimeAsync(499)

    expect(updateItemMock).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(1)

    expect(updateItemMock).toHaveBeenCalledTimes(1)
    expect(updateItemMock).toHaveBeenCalledWith('p1', 4, '"V1"')
  })

  it('sends the quantity immediately on blur and cancels the debounce', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    updateItemMock.mockResolvedValue({ data: cartWithQuantity(3), etag: '"V2"' })

    const wrapper = mountView()
    await flushPromises()
    const input = wrapper.findComponent(NInputNumber)

    input.vm.$emit('update:value', 3)
    await wrapper.vm.$nextTick()

    input.vm.$emit('blur')
    await flushPromises()

    expect(updateItemMock).toHaveBeenCalledTimes(1)
    expect(updateItemMock).toHaveBeenCalledWith('p1', 3, '"V1"')

    await vi.advanceTimersByTimeAsync(500)

    expect(updateItemMock).toHaveBeenCalledTimes(1)
  })

  it('flushes a pending quantity change on unmount', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    updateItemMock.mockResolvedValue({ data: cartWithQuantity(3), etag: '"V2"' })

    const wrapper = mountView()
    await flushPromises()
    const input = wrapper.findComponent(NInputNumber)

    input.vm.$emit('update:value', 3)
    await wrapper.vm.$nextTick()
    expect(updateItemMock).not.toHaveBeenCalled()

    wrapper.unmount()
    await flushPromises()

    expect(updateItemMock).toHaveBeenCalledTimes(1)
    expect(updateItemMock).toHaveBeenCalledWith('p1', 3, '"V1"')
  })

  it('syncs the draft back to the server quantity when the update fails', async () => {
    getCartMock.mockResolvedValue({ data: cart, etag: '"V1"' })
    updateItemMock.mockRejectedValue({
      status: 500,
      detail: 'Не удалось обновить корзину',
      raw: null,
    } as ApiError)

    const wrapper = mountView()
    await flushPromises()
    const input = wrapper.findComponent(NInputNumber)

    input.vm.$emit('update:value', 3)
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.cart-item__line-total').text()).toBe('$28.50')

    input.vm.$emit('blur')
    await flushPromises()

    expect(updateItemMock).toHaveBeenCalledWith('p1', 3, '"V1"')
    expect(wrapper.find('.cart-item__line-total').text()).toBe('$19.00')
    expect(input.props('value')).toBe(2)
  })
})
