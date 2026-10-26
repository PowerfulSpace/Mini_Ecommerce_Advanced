import { beforeEach, describe, expect, it, vi } from 'vitest'
import { h } from 'vue'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { NMessageProvider } from 'naive-ui'
import { getProduct } from '@/services/api/products'
import { addItem, getCart } from '@/services/api/cart'
import ProductView from '@/views/ProductView.vue'
import type { ApiError, Product } from '@/types/api'

vi.mock('@/services/api/products', () => ({
  getProducts: vi.fn(),
  getProduct: vi.fn(),
}))

vi.mock('@/services/api/cart', () => ({
  getCart: vi.fn(),
  addItem: vi.fn(),
  updateItem: vi.fn(),
  removeItem: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: 'p1' } }),
}))

const getProductMock = vi.mocked(getProduct)
const getCartMock = vi.mocked(getCart)
const addItemMock = vi.mocked(addItem)

const product: Product = {
  id: 'p1',
  name: 'Test Keyboard',
  description: 'A split keyboard',
  price: 9.5,
  stockQuantity: 50,
}

// `useMessage()` requires a message provider ancestor, so the view is mounted
// inside one; `findComponent` still finds the descendant RouterLinkStub.
function mountView() {
  return mount(
    { render: () => h(NMessageProvider, null, { default: () => h(ProductView) }) },
    { global: { stubs: { RouterLink: RouterLinkStub } } },
  )
}

function findAddButton(wrapper: ReturnType<typeof mountView>) {
  return wrapper.findAll('button').find((button) => button.text().includes('В корзину'))
}

describe('ProductView', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    getProductMock.mockReset()
    getCartMock.mockReset()
    addItemMock.mockReset()
  })

  it('loads and renders the product', async () => {
    getProductMock.mockResolvedValue(product)

    const wrapper = mountView()
    await flushPromises()

    expect(getProductMock).toHaveBeenCalledWith('p1')
    expect(wrapper.text()).toContain('Test Keyboard')
    expect(wrapper.text()).toContain('A split keyboard')
    expect(wrapper.text()).toContain('$9.50')
    expect(wrapper.text()).toContain('В наличии: 50')
    expect(wrapper.findComponent(RouterLinkStub).props('to')).toBe('/')
  })

  it('renders the not-found state for product_not_found', async () => {
    getProductMock.mockRejectedValue({
      status: 404,
      code: 'product_not_found',
      raw: null,
    } as ApiError)

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Товар не найден')
  })

  it('renders the error state with a retry action', async () => {
    getProductMock.mockRejectedValue({
      status: 500,
      detail: 'Сервер недоступен',
      raw: null,
    } as ApiError)

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Сервер недоступен')
    expect(wrapper.text()).toContain('Повторить')
  })

  it('shows a "no stock" tag without hiding the product', async () => {
    getProductMock.mockResolvedValue({ ...product, stockQuantity: 0 })

    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.text()).toContain('Test Keyboard')
    expect(wrapper.text()).toContain('Нет в наличии')
  })

  it('adds the product to the cart when "В корзину" is clicked', async () => {
    getProductMock.mockResolvedValue(product)
    getCartMock.mockResolvedValue({ data: { items: [], totalAmount: 0 }, etag: '"V1"' })
    addItemMock.mockResolvedValue({ data: { items: [], totalAmount: 0 }, etag: '"V2"' })

    const wrapper = mountView()
    await flushPromises()

    await findAddButton(wrapper)!.trigger('click')
    await flushPromises()

    expect(addItemMock).toHaveBeenCalledWith('p1', 1, '"V1"')
  })
})
