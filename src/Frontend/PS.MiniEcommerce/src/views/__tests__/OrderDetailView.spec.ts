import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { NPopconfirm } from 'naive-ui'
import OrderDetailView from '@/views/OrderDetailView.vue'
import { useAuthStore } from '@/stores/auth'
import { cancelOrder, getOrder } from '@/services/api/orders'
import type { ApiError, OrderDetail } from '@/types/api'

vi.mock('@/services/api/orders', () => ({
  createOrder: vi.fn(),
  getOrders: vi.fn(),
  getOrder: vi.fn(),
  cancelOrder: vi.fn(),
}))

const { messageMock } = vi.hoisted(() => ({
  messageMock: { success: vi.fn(), warning: vi.fn(), error: vi.fn() },
}))

// `useMessage()` requires a message-provider ancestor; replacing just that hook
// keeps the real UI components while making the feedback assertions exact.
vi.mock('naive-ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('naive-ui')>()
  return { ...actual, useMessage: () => messageMock }
})

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: 'order-1' } }),
}))

const getOrderMock = vi.mocked(getOrder)
const cancelOrderMock = vi.mocked(cancelOrder)

// Snapshots deliberately differ from any plausible current catalog values so the
// test fails if the view ever reads names or prices from another source.
const order: OrderDetail = {
  id: 'order-1',
  status: 'Created',
  createdAtUtc: '2026-01-15T12:30:00Z',
  totalAmount: 19,
  items: [
    {
      productId: 'p1',
      productName: 'Исторический товар',
      unitPrice: 1.23,
      quantity: 2,
      lineTotal: 2.46,
    },
  ],
}

const cancelledOrder: OrderDetail = { ...order, status: 'Cancelled' }

function mountView() {
  return mount(OrderDetailView, {
    global: { stubs: { RouterLink: RouterLinkStub } },
  })
}

function findCancelButton(wrapper: ReturnType<typeof mountView>) {
  return wrapper.findAll('button').find((button) => button.text().includes('Отменить заказ'))
}

describe('OrderDetailView', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    getOrderMock.mockReset()
    cancelOrderMock.mockReset()
    messageMock.success.mockReset()
    messageMock.warning.mockReset()
    messageMock.error.mockReset()
  })

  it('renders the order using the snapshot name and price from the response', async () => {
    getOrderMock.mockResolvedValue(order)

    const wrapper = mountView()
    await flushPromises()

    expect(getOrderMock).toHaveBeenCalledWith('order-1')
    expect(wrapper.text()).toContain('order-1')
    expect(wrapper.text()).toContain('Исторический товар')
    expect(wrapper.text()).toContain('$1.23')
    expect(wrapper.text()).toContain('$2.46')
    expect(wrapper.text()).toContain('Итого: $19.00')
  })

  it('renders the not-found state for order_not_found', async () => {
    getOrderMock.mockRejectedValue({
      status: 404,
      code: 'order_not_found',
      raw: null,
    } as ApiError)

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Заказ не найден')
    expect(findCancelButton(wrapper)).toBeUndefined()
  })

  it('renders the error state with a retry action', async () => {
    getOrderMock.mockRejectedValue({
      status: 500,
      detail: 'Сервер недоступен',
      raw: null,
    } as ApiError)

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Сервер недоступен')
    expect(wrapper.text()).toContain('Повторить')
  })

  it('offers a link back to the order list', async () => {
    getOrderMock.mockResolvedValue(order)

    const wrapper = mountView()
    await flushPromises()

    const link = wrapper
      .findAllComponents(RouterLinkStub)
      .find((l) => l.props('to') === '/orders')
    expect(link).toBeTruthy()
  })

  it('shows the cancel button only for a Created order', async () => {
    getOrderMock.mockResolvedValue(order)

    const wrapper = mountView()
    await flushPromises()

    expect(findCancelButton(wrapper)).toBeTruthy()
  })

  it('does not show the cancel button for a Cancelled order', async () => {
    getOrderMock.mockResolvedValue(cancelledOrder)

    const wrapper = mountView()
    await flushPromises()

    expect(findCancelButton(wrapper)).toBeUndefined()
  })

  it('cancels the order after the confirmation is accepted', async () => {
    getOrderMock.mockResolvedValue(order)
    cancelOrderMock.mockResolvedValue(cancelledOrder)

    const wrapper = mountView()
    await flushPromises()

    wrapper.findComponent(NPopconfirm).vm.$emit('positive-click')
    await flushPromises()

    expect(cancelOrderMock).toHaveBeenCalledWith('order-1')
    expect(messageMock.success).toHaveBeenCalledWith('Заказ отменён')
    expect(wrapper.text()).toContain('Отменён')
    expect(findCancelButton(wrapper)).toBeUndefined()
  })

  it('does not cancel when the confirmation is declined', async () => {
    getOrderMock.mockResolvedValue(order)
    cancelOrderMock.mockResolvedValue(cancelledOrder)

    const wrapper = mountView()
    await flushPromises()

    wrapper.findComponent(NPopconfirm).vm.$emit('negative-click')
    await flushPromises()

    expect(cancelOrderMock).not.toHaveBeenCalled()
  })

  it('warns and shows the refreshed status when the order was already cancelled', async () => {
    getOrderMock.mockResolvedValueOnce(order).mockResolvedValueOnce(cancelledOrder)
    cancelOrderMock.mockRejectedValue({
      status: 409,
      code: 'order_already_cancelled',
      raw: null,
    } as ApiError)

    const wrapper = mountView()
    await flushPromises()

    wrapper.findComponent(NPopconfirm).vm.$emit('positive-click')
    await flushPromises()

    expect(messageMock.warning).toHaveBeenCalledWith('Заказ уже был отменён')
    expect(wrapper.text()).toContain('Отменён')
  })

  it('shows an error message on an unexpected cancellation failure', async () => {
    getOrderMock.mockResolvedValue(order)
    cancelOrderMock.mockRejectedValue({
      status: 500,
      detail: 'Сервер недоступен',
      raw: null,
    } as ApiError)

    const wrapper = mountView()
    await flushPromises()

    wrapper.findComponent(NPopconfirm).vm.$emit('positive-click')
    await flushPromises()

    expect(messageMock.error).toHaveBeenCalledWith('Сервер недоступен')
  })

  it('refetches the current order when the development identity changes', async () => {
    getOrderMock.mockResolvedValue(order)

    mountView()
    await flushPromises()
    expect(getOrderMock).toHaveBeenCalledTimes(1)

    useAuthStore().setUser('bob')
    await flushPromises()

    expect(getOrderMock).toHaveBeenCalledTimes(2)
    expect(getOrderMock).toHaveBeenLastCalledWith('order-1')
  })
})
