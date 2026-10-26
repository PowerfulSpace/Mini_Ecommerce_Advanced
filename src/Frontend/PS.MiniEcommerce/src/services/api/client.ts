import axios, { type AxiosError } from 'axios'
import { useAuthStore } from '@/stores/auth'
import type { ApiError } from '@/types/api'

declare module 'axios' {
  export interface AxiosRequestConfig {
    /**
     * When true, the Development-only `X-Test-User` header is not attached.
     * Public endpoints (the catalog) must be requested without an identity
     * header (FRONTEND.md section 6).
     */
    omitTestUserHeader?: boolean
  }
}

// In development the base URL is relative and resolved through the Vite proxy.
// In production it falls back to VITE_API_URL, so the backend origin is never
// hard-coded in application code.
export const API_BASE_URL: string =
  (import.meta.env.VITE_API_URL as string | undefined) ?? '/api'

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  // Checkout can be slower than the default 5s; 15s avoids spurious timeouts.
  timeout: 15000,
  headers: {
    'Content-Type': 'application/json',
  },
})

apiClient.interceptors.request.use((config) => {
  // Public requests opt out via `omitTestUserHeader` so the catalog is requested
  // without an identity header (FRONTEND.md section 6). Authenticated requests
  // receive the Development-only `X-Test-User` header when an identity is
  // selected. This is never a production authentication mechanism.
  if (config.omitTestUserHeader) {
    return config
  }

  const auth = useAuthStore()
  if (auth.testUser !== null) {
    config.headers.set('X-Test-User', auth.testUser)
  }

  return config
})

apiClient.interceptors.response.use(
  (response) => response,
  (error: AxiosError) => Promise.reject(toApiError(error)),
)

interface ProblemDetailsShape {
  code?: unknown
  title?: unknown
  detail?: unknown
  errors?: unknown
  instance?: unknown
}

// Normalizes an axios failure into a typed ApiError. `code` is left undefined
// when the backend response does not carry one (automatic model-binding 400s
// and 415s), so callers must still fall back to title/detail.
export function toApiError(error: unknown): ApiError {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as ProblemDetailsShape | undefined

    return {
      status: error.response?.status ?? 0,
      code: typeof data?.code === 'string' ? data.code : undefined,
      title: typeof data?.title === 'string' ? data.title : undefined,
      detail: typeof data?.detail === 'string' ? data.detail : undefined,
      errors: isErrorsRecord(data?.errors) ? data.errors : undefined,
      instance: typeof data?.instance === 'string' ? data.instance : undefined,
      raw: error,
    }
  }

  return { status: 0, raw: error }
}

function isErrorsRecord(value: unknown): value is Record<string, string[]> {
  return typeof value === 'object' && value !== null
}
