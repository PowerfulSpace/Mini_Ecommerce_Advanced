import { apiClient } from '@/services/api/client'
import type { Paginated, Product } from '@/types/api'

export interface ProductQuery {
  page?: number
  pageSize?: number
}

// The catalog is public: these requests are sent without an identity header.
// No response shaping happens here beyond typing; the backend returns the
// documented envelope and a single product object.
export async function getProducts(params: ProductQuery = {}): Promise<Paginated<Product>> {
  const response = await apiClient.get<Paginated<Product>>('/products', {
    params,
    omitTestUserHeader: true,
  })

  return response.data
}

export async function getProduct(id: string): Promise<Product> {
  const response = await apiClient.get<Product>(`/products/${id}`, {
    omitTestUserHeader: true,
  })

  return response.data
}
