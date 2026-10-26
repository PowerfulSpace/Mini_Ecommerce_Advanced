// API contracts mirrored from docs/en/architecture/api.md and verified against the
// backend implementation (see DECISIONS.md section 13 for known deviations).
// Only fields that exist in the contract are declared here.

export interface Paginated<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface Product {
  id: string
  name: string
  description: string
  price: number
  stockQuantity: number
}

export interface CartItem {
  productId: string
  productName: string
  unitPrice: number
  quantity: number
  lineTotal: number
  isAvailable: boolean
}

export interface Cart {
  items: CartItem[]
  totalAmount: number
}

export type OrderStatus = 'Created' | 'Cancelled'

export interface OrderSummary {
  id: string
  status: OrderStatus
  createdAtUtc: string
  totalAmount: number
}

export interface OrderItem {
  productId: string
  productName: string
  unitPrice: number
  quantity: number
  lineTotal: number
}

export interface OrderDetail extends OrderSummary {
  items: OrderItem[]
}

export interface HealthResponse {
  status: string
}

// Normalized representation of a failed API call. `code` is optional because
// automatic model-binding 400s and 415s may not carry a stable code
// (see DECISIONS.md section 13).
export interface ApiError {
  status: number
  code?: string
  title?: string
  detail?: string
  errors?: Record<string, string[]>
  instance?: string
  raw: unknown
}
