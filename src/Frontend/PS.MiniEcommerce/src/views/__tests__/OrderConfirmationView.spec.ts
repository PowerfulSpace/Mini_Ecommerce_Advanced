import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import OrderConfirmationView from '@/views/OrderConfirmationView.vue'

const { replaceMock } = vi.hoisted(() => ({ replaceMock: vi.fn() }))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: 'order-1' } }),
  useRouter: () => ({ replace: replaceMock }),
}))

describe('OrderConfirmationView', () => {
  beforeEach(() => {
    replaceMock.mockReset()
  })

  it('redirects to the order details screen on mount', async () => {
    mount(OrderConfirmationView)
    await flushPromises()

    // Render nothing here: /orders/:id is the single source of truth (decision A).
    expect(replaceMock).toHaveBeenCalledWith({
      name: 'order-detail',
      params: { id: 'order-1' },
    })
  })
})
