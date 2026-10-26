import { apiClient } from '@/services/api/client'
import type { OrderDetail, OrderSummary, Paginated } from '@/types/api'

export interface OrderQuery {
  page?: number
  pageSize?: number
}

// Checkout requires both headers: `Idempotency-Key` identifies the attempt and
// `If-Match` carries the cart ETag, which is part of the request fingerprint
// (api.md section 3.4, and DECISIONS.md section 13 D1). The request body is
// empty. Failures are rejected as ApiError by the shared response interceptor;
// no special handling is added here.
export async function createOrder(
  idempotencyKey: string,
  ifMatch: string,
): Promise<OrderDetail> {
  // The body is intentionally empty. `undefined` (not `null`) is passed because
  // axios serializes a `null` body to the four-byte string "null" when the JSON
  // content type is set, which would contradict the documented empty body.
  const response = await apiClient.post<OrderDetail>('/orders', undefined, {
    headers: {
      'Idempotency-Key': idempotencyKey,
      'If-Match': ifMatch,
    },
  })

  return response.data
}

// Both read endpoints require authentication, so the shared request interceptor
// attaches the development identity header. Failures are normalized to ApiError
// by the response interceptor; no response shaping happens here.
export async function getOrders(
  params: OrderQuery = {},
): Promise<Paginated<OrderSummary>> {
  const response = await apiClient.get<Paginated<OrderSummary>>('/orders', { params })

  return response.data
}

export async function getOrder(id: string): Promise<OrderDetail> {
  const response = await apiClient.get<OrderDetail>(`/orders/${id}`)

  return response.data
}

// Cancellation is guarded by a conditional order-status transition on the
// backend, so it sends neither `If-Match` nor `Idempotency-Key` (api.md section
// 3.4). The request body is empty (`undefined`, not `null`, so axios does not
// serialize it to the four-byte string "null"); failures are normalized to
// ApiError by the shared response interceptor.
export async function cancelOrder(id: string): Promise<OrderDetail> {
  const response = await apiClient.post<OrderDetail>(`/orders/${id}/cancel`, undefined)

  return response.data
}
