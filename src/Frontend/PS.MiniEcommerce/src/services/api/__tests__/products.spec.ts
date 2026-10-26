import { afterEach, describe, expect, it } from 'vitest'
import { AxiosError, type AxiosAdapter, type InternalAxiosRequestConfig } from 'axios'
import { apiClient } from '@/services/api/client'
import { getProduct, getProducts } from '@/services/api/products'
import type { Paginated, Product } from '@/types/api'

const originalAdapter = apiClient.defaults.adapter

afterEach(() => {
  apiClient.defaults.adapter = originalAdapter
})

function resolveWith(payload: unknown): AxiosAdapter {
  return async (config: InternalAxiosRequestConfig) => ({
    data: payload,
    status: 200,
    statusText: 'OK',
    headers: {},
    config,
  })
}

describe('products API', () => {
  it('getProducts returns the typed pagination envelope and forwards query params', async () => {
    const envelope: Paginated<Product> = {
      items: [
        { id: 'p1', name: 'Keyboard', description: 'Split keyboard', price: 9.5, stockQuantity: 50 },
      ],
      page: 2,
      pageSize: 5,
      totalCount: 6,
    }

    let seenUrl: string | undefined
    let seenParams: unknown
    apiClient.defaults.adapter = async (config) => {
      seenUrl = config.url
      seenParams = config.params

      return { data: envelope, status: 200, statusText: 'OK', headers: {}, config }
    }

    const result = await getProducts({ page: 2, pageSize: 5 })

    expect(result).toEqual(envelope)
    expect(seenUrl).toBe('/products')
    expect(seenParams).toEqual({ page: 2, pageSize: 5 })
  })

  it('getProduct returns a single product', async () => {
    const product: Product = {
      id: 'p1',
      name: 'Keyboard',
      description: 'Split keyboard',
      price: 9.5,
      stockQuantity: 50,
    }
    apiClient.defaults.adapter = resolveWith(product)

    await expect(getProduct('p1')).resolves.toEqual(product)
  })

  it('converts an axios failure into an ApiError', async () => {
    apiClient.defaults.adapter = async (config) => {
      throw new AxiosError('Not Found', 'ERR_BAD_RESPONSE', config, undefined, {
        data: { status: 404, title: 'Not Found', code: 'product_not_found' },
        status: 404,
        statusText: 'Not Found',
        headers: {},
        config,
      })
    }

    await expect(getProduct('missing')).rejects.toMatchObject({
      status: 404,
      code: 'product_not_found',
      title: 'Not Found',
    })
  })
})
