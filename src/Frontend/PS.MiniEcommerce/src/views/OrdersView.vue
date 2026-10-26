<template>
  <div class="orders">
    <h2>Мои заказы</h2>

    <n-spin :show="store.loading">
      <div v-if="store.error" class="orders__state">
        <n-alert type="error">{{ errorMessage }}</n-alert>
        <n-button type="primary" @click="reload">Повторить</n-button>
      </div>

      <n-empty v-else-if="store.isEmpty" class="orders__state" description="Заказов пока нет">
        <template #extra>
          <router-link to="/">Перейти в каталог</router-link>
        </template>
      </n-empty>

      <template v-else>
        <div class="orders__list">
          <n-card
            v-for="order in store.items"
            :key="order.id"
            hoverable
            class="order-row"
            @click="openOrder(order.id)"
          >
            <div class="order-row__main">
              <span class="order-row__id" :title="order.id">{{ shortId(order.id) }}</span>
              <span class="order-row__date">{{ formatDate(order.createdAtUtc) }}</span>
            </div>
            <div class="order-row__meta">
              <n-tag size="small" :type="statusType(order.status)">
                {{ statusLabel(order.status) }}
              </n-tag>
              <span class="order-row__total">{{ formatPrice(order.totalAmount) }}</span>
            </div>
          </n-card>
        </div>

        <div v-if="showPagination" class="orders__pagination">
          <n-pagination
            :page="store.page"
            :page-size="store.pageSize"
            :item-count="store.totalCount"
            :disabled="store.loading"
            @update:page="onPageChange"
          />
        </div>
      </template>
    </n-spin>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { NAlert, NButton, NCard, NEmpty, NPagination, NSpin, NTag } from 'naive-ui'
import { useOrdersStore } from '@/stores/orders'
import { useUserScopedFetch } from '@/composables/useUserScopedFetch'
import type { OrderStatus } from '@/types/api'

const store = useOrdersStore()
const router = useRouter()

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

function shortId(id: string): string {
  return id.length > 8 ? `${id.slice(0, 8)}…` : id
}

function statusType(status: OrderStatus): 'success' | 'warning' {
  return status === 'Cancelled' ? 'warning' : 'success'
}

function statusLabel(status: OrderStatus): string {
  return status === 'Cancelled' ? 'Отменён' : 'Создан'
}

const errorMessage = computed(
  () => store.error?.detail || store.error?.title || 'Не удалось загрузить заказы',
)

const showPagination = computed(() => !store.error && store.totalCount > 0)

function openOrder(id: string): void {
  void router.push({ name: 'order-detail', params: { id } })
}

function onPageChange(target: number): void {
  void store.fetchPage(target)
}

function reload(): void {
  void store.fetchPage(store.page)
}

onMounted(() => {
  void store.fetchPage(1)
})

useUserScopedFetch(() => store.fetchPage(store.page || 1))
</script>

<style scoped>
.orders__state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
}

.orders__list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.order-row {
  cursor: pointer;
}

.order-row :deep(.n-card__content) {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.order-row__main {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.order-row__id {
  font-family: monospace;
  font-weight: 600;
}

.order-row__date {
  color: var(--n-text-color-3, #666);
}

.order-row__meta {
  display: flex;
  align-items: center;
  gap: 16px;
}

.order-row__total {
  font-size: 1.1rem;
  font-weight: 600;
}

.orders__pagination {
  display: flex;
  justify-content: center;
  margin-top: 24px;
}
</style>
