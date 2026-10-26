<template>
  <div class="order-detail">
    <router-link to="/orders" class="order-detail__back">← Назад к заказам</router-link>

    <n-spin :show="store.loading">
      <n-result
        v-if="store.notFound"
        status="404"
        title="Заказ не найден"
        description="Заказ не существует или принадлежит другому пользователю."
      >
        <template #footer>
          <router-link to="/orders">Вернуться к заказам</router-link>
        </template>
      </n-result>

      <div v-else-if="store.error" class="order-detail__state">
        <n-alert type="error">{{ errorMessage }}</n-alert>
        <n-button type="primary" @click="reload">Повторить</n-button>
      </div>

      <n-card v-else-if="store.order" class="order-detail__card">
        <div class="order-detail__header">
          <div class="order-detail__header-main">
            <span class="order-detail__id">{{ store.order.id }}</span>
            <span class="order-detail__date">{{ formatDate(store.order.createdAtUtc) }}</span>
          </div>
          <div class="order-detail__header-actions">
            <n-tag size="small" :type="statusType(store.order.status)">
              {{ statusLabel(store.order.status) }}
            </n-tag>
            <n-popconfirm
              v-if="showCancel"
              positive-text="Да, отменить"
              negative-text="Нет"
              @positive-click="onCancel"
            >
              <template #trigger>
                <n-button
                  type="error"
                  secondary
                  :loading="store.cancelling"
                  :disabled="store.cancelling || store.loading"
                >
                  Отменить заказ
                </n-button>
              </template>
              Отменить заказ? Действие необратимо.
            </n-popconfirm>
          </div>
        </div>

        <table class="order-items">
          <thead>
            <tr>
              <th>Товар</th>
              <th class="order-items__num">Цена</th>
              <th class="order-items__num">Кол-во</th>
              <th class="order-items__num">Сумма</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in store.order.items" :key="item.productId">
              <td>{{ item.productName }}</td>
              <td class="order-items__num">{{ formatPrice(item.unitPrice) }}</td>
              <td class="order-items__num">{{ item.quantity }}</td>
              <td class="order-items__num">{{ formatPrice(item.lineTotal) }}</td>
            </tr>
          </tbody>
        </table>

        <div class="order-detail__total">
          Итого: {{ formatPrice(store.order.totalAmount) }}
        </div>
      </n-card>
    </n-spin>
  </div>
</template>

<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import { NAlert, NButton, NCard, NPopconfirm, NResult, NSpin, NTag, useMessage } from 'naive-ui'
import { useOrderDetailStore } from '@/stores/orderDetail'
import { useUserScopedFetch } from '@/composables/useUserScopedFetch'
import type { OrderStatus } from '@/types/api'

const route = useRoute()
const store = useOrderDetailStore()
const message = useMessage()

const usdFormatter = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

// UI copy is Russian, so dates use the Russian locale.
const dateFormatter = new Intl.DateTimeFormat('ru-RU', {
  dateStyle: 'short',
  timeStyle: 'short',
})

function formatPrice(value: number): string {
  return usdFormatter.format(value)
}

function formatDate(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : dateFormatter.format(parsed)
}

function statusType(status: OrderStatus): 'success' | 'warning' {
  return status === 'Cancelled' ? 'warning' : 'success'
}

function statusLabel(status: OrderStatus): string {
  return status === 'Cancelled' ? 'Отменён' : 'Создан'
}

const errorMessage = computed(
  () => store.error?.detail || store.error?.title || 'Не удалось загрузить заказ',
)

// Visibility is tied to the order status, while `canCancel` additionally gates
// the button while a cancellation is already running (disabled, not removed).
const showCancel = computed(() => store.order?.status === 'Created' && !store.notFound)

function currentId(): string {
  const raw = route.params.id
  return Array.isArray(raw) ? (raw[0] ?? '') : (raw ?? '')
}

function reload(): void {
  void store.fetch(currentId())
}

async function onCancel(): Promise<void> {
  const previousStatus = store.order?.status

  await store.cancel()

  if (store.cancelError?.code === 'order_not_found') {
    // The not-found screen takes over; no toast is needed.
    return
  }

  if (store.cancelError?.code === 'order_already_cancelled') {
    message.warning('Заказ уже был отменён')
    return
  }

  if (store.cancelError) {
    message.error(store.cancelError.detail || store.cancelError.title || 'Не удалось отменить заказ')
    return
  }

  if (previousStatus === 'Created' && store.order?.status === 'Cancelled') {
    message.success('Заказ отменён')
  }
}

watch(
  () => route.params.id,
  () => {
    void store.fetch(currentId())
  },
  { immediate: true },
)

useUserScopedFetch(() => store.fetch(currentId()))
</script>

<style scoped>
.order-detail__back {
  display: inline-block;
  margin-bottom: 16px;
}

.order-detail__state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
}

.order-detail__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 20px;
  flex-wrap: wrap;
}

.order-detail__header-main {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.order-detail__header-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}

.order-detail__id {
  font-family: monospace;
  font-weight: 600;
  word-break: break-all;
}

.order-detail__date {
  color: var(--n-text-color-3, #666);
}

.order-items {
  width: 100%;
  border-collapse: collapse;
}

.order-items th,
.order-items td {
  padding: 8px 12px;
  border-bottom: 1px solid var(--n-border-color, #efefef);
  text-align: left;
}

.order-items__num {
  text-align: right;
}

.order-detail__total {
  margin-top: 20px;
  text-align: right;
  font-size: 1.2rem;
  font-weight: 600;
}
</style>
