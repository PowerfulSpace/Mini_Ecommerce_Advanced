import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { NDrawer } from 'naive-ui'
import Header from '@/components/layout/Header.vue'

type ChangeListener = (event: MediaQueryListEvent) => void

const routerMock = vi.hoisted(() => ({
  push: vi.fn(),
  route: { name: 'catalog', fullPath: '/' } as { name?: string; fullPath: string },
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: routerMock.push }),
  useRoute: () => routerMock.route,
}))

const mediaQueryListeners = new Set<ChangeListener>()
let mediaQueryMatches = false

function installMatchMedia() {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: vi.fn((query: string) => ({
      matches: mediaQueryMatches,
      media: query,
      onchange: null,
      addEventListener: (_event: string, listener: ChangeListener) =>
        mediaQueryListeners.add(listener),
      removeEventListener: (_event: string, listener: ChangeListener) =>
        mediaQueryListeners.delete(listener),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  })
}

function setMobile(matches: boolean) {
  mediaQueryMatches = matches
  const event = { matches } as MediaQueryListEvent
  mediaQueryListeners.forEach((listener) => listener(event))
}

let wrapper: ReturnType<typeof mount> | null = null

function mountHeader() {
  wrapper = mount(Header, {
    global: { stubs: { RouterLink: RouterLinkStub } },
    attachTo: document.body,
  })
  return wrapper
}

function queryDrawer() {
  return document.body.querySelector('.n-drawer')
}

function isMenuItemSelected(container: ParentNode, label: string) {
  const item = Array.from(container.querySelectorAll('.n-menu-item-content')).find(
    (element) => element.textContent?.trim() === label,
  )
  return item?.classList.contains('n-menu-item-content--selected') ?? false
}

describe('Header', () => {
  beforeEach(() => {
    mediaQueryMatches = false
    mediaQueryListeners.clear()
    installMatchMedia()
    setActivePinia(createPinia())
    routerMock.push.mockReset()
    routerMock.route = { name: 'catalog', fullPath: '/' }
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
    vi.restoreAllMocks()
  })

  it('shows the horizontal menu and no burger on desktop', () => {
    const header = mountHeader()

    expect(header.find('.n-menu--horizontal').exists()).toBe(true)
    expect(header.find('button[aria-label="Открыть меню"]').exists()).toBe(false)
  })

  it('shows the burger and no horizontal menu on mobile', () => {
    mediaQueryMatches = true
    const header = mountHeader()

    expect(header.find('button[aria-label="Открыть меню"]').exists()).toBe(true)
    expect(header.find('.n-menu--horizontal').exists()).toBe(false)
  })

  it('opens the drawer when the burger is clicked', async () => {
    mediaQueryMatches = true
    const header = mountHeader()

    await header.get('button[aria-label="Открыть меню"]').trigger('click')
    await flushPromises()

    expect(header.get('button[aria-label="Открыть меню"]').attributes('aria-expanded')).toBe('true')
    expect(queryDrawer()).not.toBeNull()
    expect(queryDrawer()?.id).toBe('mobile-nav-drawer')
  })

  it('navigates and closes the drawer when a drawer item is clicked', async () => {
    mediaQueryMatches = true
    const header = mountHeader()

    await header.get('button[aria-label="Открыть меню"]').trigger('click')
    await flushPromises()

    const drawer = queryDrawer()
    expect(drawer).not.toBeNull()

    const cartItem = Array.from(drawer!.querySelectorAll('.n-menu-item-content')).find(
      (element) => element.textContent?.trim() === 'Cart',
    )
    expect(cartItem).toBeDefined()
    cartItem!.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()

    expect(routerMock.push).toHaveBeenCalledWith({ name: 'cart' })
    expect(header.get('button[aria-label="Открыть меню"]').attributes('aria-expanded')).toBe('false')
  })

  it('keeps the Orders item selected on the order detail route', () => {
    routerMock.route = { name: 'order-detail', fullPath: '/orders/1' }
    const header = mountHeader()

    expect(isMenuItemSelected(header.element, 'Orders')).toBe(true)
    expect(isMenuItemSelected(header.element, 'Catalog')).toBe(false)
  })

  it('renders the dev identity selector in the header on desktop', () => {
    const header = mountHeader()

    expect(header.find('.dev-identity').exists()).toBe(true)
    expect(header.find('.dev-identity .n-select').exists()).toBe(true)
  })

  it('renders the dev identity selector inside the drawer on mobile', async () => {
    mediaQueryMatches = true
    const header = mountHeader()

    expect(header.find('.dev-identity').exists()).toBe(false)

    await header.get('button[aria-label="Открыть меню"]').trigger('click')
    await flushPromises()

    expect(queryDrawer()?.querySelector('.dev-identity')).not.toBeNull()
    expect(queryDrawer()?.querySelector('.dev-identity .n-select')).not.toBeNull()
  })

  it('closes the drawer when the viewport becomes wide', async () => {
    mediaQueryMatches = true
    const header = mountHeader()

    await header.get('button[aria-label="Открыть меню"]').trigger('click')
    await flushPromises()
    expect(header.findComponent(NDrawer).props('show')).toBe(true)

    setMobile(false)
    await flushPromises()

    expect(header.findComponent(NDrawer).props('show')).toBe(false)
    expect(header.find('.n-menu--horizontal').exists()).toBe(true)
    expect(header.find('button[aria-label="Открыть меню"]').exists()).toBe(false)
  })

  it('reacts to media query changes', async () => {
    const header = mountHeader()
    expect(header.find('.n-menu--horizontal').exists()).toBe(true)

    setMobile(true)
    await flushPromises()
    expect(header.find('button[aria-label="Открыть меню"]').exists()).toBe(true)

    setMobile(false)
    await flushPromises()
    expect(header.find('.n-menu--horizontal').exists()).toBe(true)
  })

  it('unsubscribes from the media query on unmount', () => {
    const header = mountHeader()
    expect(mediaQueryListeners.size).toBe(1)

    header.unmount()
    wrapper = null

    expect(mediaQueryListeners.size).toBe(0)
  })

  it('falls back to the desktop layout when matchMedia is unavailable', () => {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      writable: true,
      value: undefined,
    })
    const header = mountHeader()

    expect(header.find('.n-menu--horizontal').exists()).toBe(true)
    expect(header.find('button[aria-label="Открыть меню"]').exists()).toBe(false)
  })
})
