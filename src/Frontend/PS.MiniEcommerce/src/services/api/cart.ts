import type { AxiosResponse } from 'axios'
import { apiClient } from '@/services/api/client'
import type { Cart } from '@/types/api'

export interface CartResponse {
  data: Cart
  etag: string
}

// The ETag is a required part of the cart contract: without it no further
// mutation can be sent. Callers use apiClient directly (not a data-only
// wrapper) so the response headers stay accessible to this module.
function toCartResponse(response: AxiosResponse<Cart>): CartResponse {
  const etag = response.headers['etag']

  if (typeof etag !== 'string' || etag.length === 0) {
    throw new Error('The cart response did not include the required ETag header.')
  }

  return { data: response.data, etag }
}

export async function getCart(): Promise<CartResponse> {
  return toCartResponse(await apiClient.get<Cart>('/cart'))
}

export async function addItem(
  productId: string,
  quantity: number,
  etag: string,
): Promise<CartResponse> {
  return toCartResponse(
    await apiClient.post<Cart>(
      '/cart/items',
      { productId, quantity },
      { headers: { 'If-Match': etag } },
    ),
  )
}

export async function updateItem(
  productId: string,
  quantity: number,
  etag: string,
): Promise<CartResponse> {
  return toCartResponse(
    await apiClient.put<Cart>(
      `/cart/items/${productId}`,
      { quantity },
      { headers: { 'If-Match': etag } },
    ),
  )
}

// Removal uses the dedicated endpoint with the version carried only in
// `If-Match`; the request intentionally has no body.
export async function removeItem(productId: string, etag: string): Promise<CartResponse> {
  return toCartResponse(
    await apiClient.delete<Cart>(`/cart/items/${productId}`, {
      headers: { 'If-Match': etag },
    }),
  )
}
