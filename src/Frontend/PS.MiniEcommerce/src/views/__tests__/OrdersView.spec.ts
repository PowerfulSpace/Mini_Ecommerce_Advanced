import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import OrdersView from '@/views/OrdersView.vue'
import { useAuthStore } from '@/stores/auth'
import { useOrdersStore } from '@/stores/orders'
import { getOrders } from '@/services/api/orders'
import type { ApiError, OrderSummary, Paginated } from '@/types/api'

vi.mock('@/services/api/orders', () => ({
  createOrder: vi.fn(),
  getOrders: vi.fn(),
  getOrder: vi.fn(),
}))

const { pushMock } = vi.hoisted(() => ({ pushMock: vi.fn() }))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: pushMock }),
}))

const getOrdersMock = vi.mocked(getOrders)

const order: OrderSummary = {
  id: 'order-1234567890',
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

function mountView() {
  return mount(OrdersView, {
    global: { stubs: { RouterLink: RouterLinkStub } },
  })
}

describe('OrdersView', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    getOrdersMock.mockReset()
    pushMock.mockReset()
  })

  it('loads page 1 on mount and renders the orders', async () => {
    getOrdersMock.mockResolvedValue(page([order]))

    const wrapper = mountView()
    await flushPromises()

    expect(getOrdersMock).toHaveBeenCalledWith({ page: 1, pageSize: 20 })
    expect(wrapper.text()).toContain('order-12…')
    expect(wrapper.text()).toContain('Создан')
    expect(wrapper.text()).toContain('$19.00')
  })

  it('renders the empty state with a link to the catalog', async () => {
    getOrdersMock.mockResolvedValue(page([]))

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Заказов пока нет')
    const link = wrapper.findAllComponents(RouterLinkStub).find((l) => l.props('to') === '/')
    expect(link).toBeTruthy()
  })

  it('sets loading while the request is in flight', () => {
    getOrdersMock.mockReturnValue(new Promise(() => {}))

    mountView()

    expect(useOrdersStore().loading).toBe(true)
  })

  it('renders the error state with a retry action', async () => {
    getOrdersMock.mockRejectedValue({
      status: 500,
      detail: 'Сервер недоступен',
      raw: null,
    } as ApiError)

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Сервер недоступен')
    expect(wrapper.text()).toContain('Повторить')
  })

  it('navigates to the order details when a row is clicked', async () => {
    getOrdersMock.mockResolvedValue(page([order]))

    const wrapper = mountView()
    await flushPromises()

    await wrapper.find('.order-row').trigger('click')

    expect(pushMock).toHaveBeenCalledWith({
      name: 'order-detail',
      params: { id: order.id },
    })
  })

  it('refetches the current page when the development identity changes', async () => {
    getOrdersMock.mockResolvedValue(page([order]))

    mountView()
    await flushPromises()
    expect(getOrdersMock).toHaveBeenCalledTimes(1)

    useAuthStore().setUser('bob')
    await flushPromises()

    expect(getOrdersMock).toHaveBeenCalledTimes(2)
    expect(getOrdersMock).toHaveBeenLastCalledWith({ page: 1, pageSize: 20 })
  })
})
