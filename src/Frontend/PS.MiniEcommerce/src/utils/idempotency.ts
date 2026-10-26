// Outstanding checkout idempotency key. The key must survive a retry of the same
// checkout attempt (see FRONTEND.md section 9.1 and ADR-002), so it is persisted
// outside the store lifecycle. sessionStorage is used because the key is scoped
// to a single browser tab/session and must not leak across tabs.

export const KEY_STORAGE_NAME = 'mini-ecommerce:checkout-key'

// Documented key format (DECISIONS.md section 10).
const KEY_PATTERN = /^[A-Za-z0-9_-]{8,128}$/

// Fallback when sessionStorage is unavailable (SSR, private mode, storage
// disabled). Retry with the same key still works within the page lifetime.
const memory = new Map<string, string>()

function getStorage(): Storage | null {
  try {
    if (typeof sessionStorage === 'undefined') {
      return null
    }

    // Some environments expose sessionStorage but throw on access or write.
    const probeKey = '__mini_ecommerce_storage_probe__'
    sessionStorage.setItem(probeKey, '1')
    sessionStorage.removeItem(probeKey)
    return sessionStorage
  } catch {
    return null
  }
}

function read(): string | null {
  const storage = getStorage()
  if (storage === null) {
    return memory.get(KEY_STORAGE_NAME) ?? null
  }

  return storage.getItem(KEY_STORAGE_NAME)
}

function write(value: string): void {
  const storage = getStorage()
  if (storage === null) {
    memory.set(KEY_STORAGE_NAME, value)
    return
  }

  storage.setItem(KEY_STORAGE_NAME, value)
}

function remove(): void {
  const storage = getStorage()
  if (storage === null) {
    memory.delete(KEY_STORAGE_NAME)
    return
  }

  storage.removeItem(KEY_STORAGE_NAME)
}

function generateKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    // UUID v4 is hex + hyphens, which already satisfies KEY_PATTERN and its
    // 36-character length fits the 8..128 range.
    return crypto.randomUUID()
  }

  const bytes = new Uint8Array(16)
  crypto.getRandomValues(bytes)
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
}

function isValidKey(value: string | null): value is string {
  return value !== null && KEY_PATTERN.test(value)
}

export function getOrCreateKey(): string {
  const existing = read()
  if (isValidKey(existing)) {
    return existing
  }

  const key = generateKey()
  write(key)
  return key
}

export function peekKey(): string | null {
  return read()
}

export function clearKey(): void {
  remove()
}
