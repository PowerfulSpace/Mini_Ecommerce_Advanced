import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { getProducts } from '@/services/api/products'
import { useCatalogStore } from '@/stores/catalog'
import CatalogView from '@/views/CatalogView.vue'
import type { ApiError, Product } from '@/types/api'

vi.mock('@/services/api/products', () => ({
  getProducts: vi.fn(),
  getProduct: vi.fn(),
}))

const getProductsMock = vi.mocked(getProducts)

const product: Product = {
  id: 'p1',
  name: 'Test Keyboard',
  description: 'A split keyboard',
  price: 9.5,
  stockQuantity: 50,
}

function mountView() {
  return mount(CatalogView, {
    global: { stubs: { RouterLink: RouterLinkStub } },
  })
}

describe('CatalogView', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    getProductsMock.mockReset()
  })

  it('loads page 1 on mount and renders the products', async () => {
    getProductsMock.mockResolvedValue({ items: [product], page: 1, pageSize: 20, totalCount: 1 })

    const wrapper = mountView()
    await flushPromises()

    expect(getProductsMock).toHaveBeenCalledWith({ page: 1, pageSize: 20 })
    expect(wrapper.text()).toContain('Test Keyboard')
    expect(wrapper.text()).toContain('$9.50')
    expect(wrapper.text()).toContain('Остаток: 50')
    expect(wrapper.findComponent(RouterLinkStub).props('to')).toEqual({
      name: 'product',
      params: { id: 'p1' },
    })
  })

  it('renders the error state with a retry action', async () => {
    getProductsMock.mockRejectedValue({
      status: 500,
      detail: 'Сервер недоступен',
      raw: null,
    } as ApiError)

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Сервер недоступен')
    expect(wrapper.text()).toContain('Повторить')
  })

  it('renders the empty state when there are no products', async () => {
    getProductsMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 })

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Каталог пуст')
  })

  it('reloads the current page on mount so pagination is preserved', async () => {
    getProductsMock.mockResolvedValue({ items: [product], page: 3, pageSize: 20, totalCount: 50 })
    useCatalogStore().page = 3

    mountView()
    await flushPromises()

    expect(getProductsMock).toHaveBeenCalledWith({ page: 3, pageSize: 20 })
  })
})
