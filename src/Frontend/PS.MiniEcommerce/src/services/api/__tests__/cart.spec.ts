import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import { createPinia, setActivePinia } from 'pinia'
import { apiClient } from '@/services/api/client'
import { addItem, getCart, removeItem, updateItem } from '@/services/api/cart'
import type { Cart } from '@/types/api'

const originalAdapter = apiClient.defaults.adapter

beforeEach(() => {
  // Cart requests are authenticated, so the request interceptor reaches for the
  // auth store; an active Pinia is therefore required.
  localStorage.clear()
  setActivePinia(createPinia())
})

afterEach(() => {
  apiClient.defaults.adapter = originalAdapter
})

const cart: Cart = {
  items: [
    {
      productId: 'p1',
      productName: 'Keyboard',
      unitPrice: 9.5,
      quantity: 2,
      lineTotal: 19,
      isAvailable: true,
    },
  ],
  totalAmount: 19,
}

function resolveWith(payload: Cart, etag?: string): AxiosAdapter {
  return async (config: InternalAxiosRequestConfig) => ({
    data: payload,
    status: 200,
    statusText: 'OK',
    headers: etag === undefined ? {} : { etag },
    config,
  })
}

function parseBody(data: unknown): unknown {
  return typeof data === 'string' ? JSON.parse(data) : data
}

describe('cart API', () => {
  it('getCart returns the cart together with the ETag header', async () => {
    apiClient.defaults.adapter = resolveWith(cart, '"AAAAAAAARmQ="')

    await expect(getCart()).resolves.toEqual({ data: cart, etag: '"AAAAAAAARmQ="' })
  })

  it('addItem sends If-Match and the item body and returns the new ETag', async () => {
    let seen: { url?: string; method?: string; ifMatch?: unknown; data?: unknown } = {}
    apiClient.defaults.adapter = async (config) => {
      seen = {
        url: config.url,
        method: config.method,
        ifMatch: config.headers.get('If-Match'),
        data: config.data,
      }
      return { data: cart, status: 200, statusText: 'OK', headers: { etag: '"NEW"' }, config }
    }

    const result = await addItem('p1', 2, '"OLD"')

    expect(seen.url).toBe('/cart/items')
    expect(seen.method).toBe('post')
    expect(seen.ifMatch).toBe('"OLD"')
    expect(parseBody(seen.data)).toEqual({ productId: 'p1', quantity: 2 })
    expect(result.etag).toBe('"NEW"')
  })

  it('updateItem sends If-Match and the quantity body', async () => {
    let seen: { url?: string; method?: string; ifMatch?: unknown; data?: unknown } = {}
    apiClient.defaults.adapter = async (config) => {
      seen = {
        url: config.url,
        method: config.method,
        ifMatch: config.headers.get('If-Match'),
        data: config.data,
      }
      return { data: cart, status: 200, statusText: 'OK', headers: { etag: '"NEW"' }, config }
    }

    await updateItem('p1', 5, '"OLD"')

    expect(seen.url).toBe('/cart/items/p1')
    expect(seen.method).toBe('put')
    expect(seen.ifMatch).toBe('"OLD"')
    expect(parseBody(seen.data)).toEqual({ quantity: 5 })
  })

  it('removeItem sends If-Match and no body', async () => {
    let seen: { url?: string; method?: string; ifMatch?: unknown; data?: unknown } = {}
    apiClient.defaults.adapter = async (config) => {
      seen = {
        url: config.url,
        method: config.method,
        ifMatch: config.headers.get('If-Match'),
        data: config.data,
      }
      return { data: cart, status: 200, statusText: 'OK', headers: { etag: '"NEW"' }, config }
    }

    await removeItem('p1', '"OLD"')

    expect(seen.url).toBe('/cart/items/p1')
    expect(seen.method).toBe('delete')
    expect(seen.ifMatch).toBe('"OLD"')
    expect(seen.data).toBeUndefined()
  })

  it('fails when the response has no ETag header', async () => {
    apiClient.defaults.adapter = resolveWith(cart)

    await expect(getCart()).rejects.toThrow(/ETag/i)
  })
})
