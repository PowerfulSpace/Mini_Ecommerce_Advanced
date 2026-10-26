<template>
  <div class="cart">
    <h2>Корзина</h2>

    <n-alert v-if="store.conflict" type="warning" class="cart__conflict">
      Изменение не применено: корзина изменилась. Показаны актуальные данные.
    </n-alert>

    <div v-if="store.error && !store.conflict" class="cart__state">
      <n-alert type="error">{{ errorMessage }}</n-alert>
      <n-button type="primary" @click="refresh">Обновить</n-button>
    </div>

    <n-alert v-if="checkout.error" type="error" class="cart__conflict">
      {{ checkoutErrorMessage }}
    </n-alert>

    <n-alert v-if="checkout.inProgress" type="info" class="cart__conflict">
      Заказ обрабатывается. Нажмите «Оформить» ещё раз через несколько секунд
    </n-alert>

    <n-spin :show="store.loading">
      <n-empty
        v-if="store.isEmpty && !store.error"
        class="cart__state"
        description="Корзина пуста"
      >
        <template #extra>
          <router-link to="/">Перейти в каталог</router-link>
        </template>
      </n-empty>

      <template v-else-if="!store.isEmpty">
        <n-card v-for="item in store.items" :key="item.productId" class="cart-item">
          <div class="cart-item__row">
            <div class="cart-item__info">
              <h3 class="cart-item__name">{{ item.productName }}</h3>
              <span class="cart-item__unit-price">{{ formatPrice(item.unitPrice) }}</span>
              <n-tag v-if="!item.isAvailable" size="small" type="error">Недоступен</n-tag>
            </div>

            <div class="cart-item__controls">
              <n-input-number
                :value="quantityOf(item)"
                :min="1"
                :step="1"
                :disabled="store.mutating || !item.isAvailable"
                class="cart-item__quantity"
                @update:value="(value) => onQuantityInput(item, value)"
                @blur="() => onQuantityCommit(item)"
              />
              <span class="cart-item__line-total">{{ formatPrice(lineTotalOf(item)) }}</span>
              <n-button secondary type="error" :disabled="store.mutating" @click="onRemove(item)">
                Удалить
              </n-button>
            </div>
          </div>
        </n-card>
      </template>
    </n-spin>

    <div v-if="!store.error" class="cart__footer">
      <span class="cart__total">Итого: {{ formatPrice(displayTotalAmount) }}</span>
      <n-button
        type="primary"
        :loading="checkout.submitting"
        :disabled="store.isEmpty || store.mutating || checkout.submitting"
        @click="onCheckout"
      >
        Оформить заказ
      </n-button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NAlert, NButton, NCard, NEmpty, NInputNumber, NSpin, NTag } from 'naive-ui'
import { useCartStore } from '@/stores/cart'
import { useCheckoutStore } from '@/stores/checkout'
import { useUserScopedFetch } from '@/composables/useUserScopedFetch'
import type { CartItem } from '@/types/api'

const store = useCartStore()
const checkout = useCheckoutStore()
const router = useRouter()

const usdFormatter = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

function formatPrice(value: number): string {
  return usdFormatter.format(value)
}

const errorMessage = computed(
  () => store.error?.detail || store.error?.title || 'Не удалось загрузить корзину',
)

const checkoutErrorMessage = computed(
  () =>
    checkout.error?.detail || checkout.error?.title || 'Не удалось оформить заказ',
)

// Local drafts let the quantity commit on blur instead of firing a mutation on
// every keystroke. The server value remains authoritative: drafts are synced
// from store.items whenever the cart or an error changes.
const drafts = reactive<Record<string, number>>({})
const commitTimers = new Map<string, ReturnType<typeof setTimeout>>()
const QUANTITY_COMMIT_DELAY_MS = 500

function quantityOf(item: CartItem): number {
  return drafts[item.productId] ?? item.quantity
}

const displayTotalAmount = computed(() =>
  store.items.reduce((sum, item) => sum + item.unitPrice * quantityOf(item), 0),
)

function lineTotalOf(item: CartItem): number {
  return item.unitPrice * quantityOf(item)
}

function syncDrafts(items: CartItem[]): void {
  for (const item of items) {
    if (drafts[item.productId] !== undefined) {
      drafts[item.productId] = item.quantity
    }
  }

  for (const productId of Object.keys(drafts)) {
    if (!items.some((item) => item.productId === productId)) {
      delete drafts[productId]
    }
  }
}

watch(
  () => store.items,
  (items) => syncDrafts(items),
  { deep: true },
)

watch(
  () => store.error,
  (error) => {
    if (error !== null) {
      syncDrafts(store.items)
    }
  },
)

watch(
  () => store.conflict,
  (conflict) => {
    if (conflict) {
      syncDrafts(store.items)
    }
  },
)

function findItem(productId: string): CartItem | undefined {
  return store.items.find((item) => item.productId === productId)
}

function cancelCommitTimer(productId: string): void {
  const timer = commitTimers.get(productId)
  if (timer === undefined) {
    return
  }

  clearTimeout(timer)
  commitTimers.delete(productId)
}

async function commitQuantity(productId: string): Promise<void> {
  const next = drafts[productId]
  if (next === undefined) {
    return
  }

  const item = findItem(productId)
  if (item === undefined || next === item.quantity) {
    delete drafts[productId]
    return
  }

  try {
    await store.updateItem(productId, next)

    if (store.conflict) {
      syncDrafts(store.items)
    }
  } catch (caught) {
    if (import.meta.env.DEV) {
      console.warn('[cart] Failed to commit a debounced quantity change.', caught)
    }
  }
}

function scheduleQuantityCommit(productId: string): void {
  cancelCommitTimer(productId)

  const next = drafts[productId]
  if (next === undefined) {
    return
  }

  const item = findItem(productId)
  if (item === undefined || next === item.quantity) {
    delete drafts[productId]
    return
  }

  const timer = setTimeout(() => {
    commitTimers.delete(productId)
    void commitQuantity(productId)
  }, QUANTITY_COMMIT_DELAY_MS)

  commitTimers.set(productId, timer)
}

function onQuantityInput(item: CartItem, value: number | null): void {
  if (typeof value === 'number') {
    drafts[item.productId] = value
    scheduleQuantityCommit(item.productId)
  }
}

async function onQuantityCommit(item: CartItem): Promise<void> {
  if (!commitTimers.has(item.productId)) {
    return
  }

  cancelCommitTimer(item.productId)
  await commitQuantity(item.productId)
}

onBeforeUnmount(() => {
  const pendingProductIds = Array.from(commitTimers.keys())

  for (const productId of pendingProductIds) {
    cancelCommitTimer(productId)
    void commitQuantity(productId)
  }
})

async function onRemove(item: CartItem): Promise<void> {
  await store.removeItem(item.productId)
}

function refresh(): void {
  void store.fetch()
}

async function onCheckout(): Promise<void> {
  try {
    await checkout.submit()
  } catch {
    // The store guards the impossible "no cart ETag" case; nothing to surface.
    return
  }

  // Navigation happens only after a confirmed 201 response.
  if (checkout.lastOrderId !== null) {
    await router.push({ name: 'checkout-success', params: { id: checkout.lastOrderId } })
  }
}

onMounted(() => {
  void store.fetch()
})

useUserScopedFetch(() => store.fetch())
</script>

<style scoped>
.cart__conflict {
  margin-bottom: 16px;
}

.cart__state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
}

.cart-item {
  margin-bottom: 12px;
}

.cart-item__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.cart-item__info {
  display: flex;
  align-items: center;
  gap: 12px;
}

.cart-item__name {
  margin: 0;
}

.cart-item__controls {
  display: flex;
  align-items: center;
  gap: 16px;
}

.cart-item__quantity {
  width: 130px;
}

.cart-item__line-total {
  min-width: 90px;
  text-align: right;
  font-weight: 600;
}

.cart__footer {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 20px;
  margin-top: 20px;
}

.cart__total {
  font-size: 1.2rem;
  font-weight: 600;
}
</style>
