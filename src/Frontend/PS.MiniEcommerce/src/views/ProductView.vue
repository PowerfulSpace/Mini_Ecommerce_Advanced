<template>
  <div class="product">
    <router-link to="/" class="product__back">← Назад в каталог</router-link>

    <n-spin :show="store.loading">
      <n-result
        v-if="store.notFound"
        status="404"
        title="Товар не найден"
        description="Товар не существует или больше не доступен."
      >
        <template #footer>
          <router-link to="/">Вернуться в каталог</router-link>
        </template>
      </n-result>

      <div v-else-if="store.error" class="product__state">
        <n-alert type="error">{{ errorMessage }}</n-alert>
        <n-button type="primary" @click="reload">Повторить</n-button>
      </div>

      <n-card v-else-if="store.product" class="product__card">
        <h1 class="product__name">{{ name }}</h1>
        <p class="product__description">{{ description }}</p>
        <div class="product__meta">
          <span class="product__price">{{ formatPrice(price) }}</span>
          <n-tag size="small" :type="inStock ? 'success' : 'warning'">
            {{ inStock ? `В наличии: ${stockQuantity}` : 'Нет в наличии' }}
          </n-tag>
        </div>
        <div class="product__actions">
          <n-button type="primary" :disabled="cart.mutating" @click="addToCart">
            В корзину
          </n-button>
        </div>
      </n-card>
    </n-spin>
  </div>
</template>

<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import { NAlert, NButton, NCard, NResult, NSpin, NTag, useMessage } from 'naive-ui'
import { useProductStore } from '@/stores/product'
import { useCartStore } from '@/stores/cart'

const route = useRoute()
const store = useProductStore()
const cart = useCartStore()
const message = useMessage()

const usdFormatter = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

function formatPrice(value: number): string {
  return usdFormatter.format(value)
}

const name = computed(() => store.product?.name ?? '')
const description = computed(() => store.product?.description ?? '')
const price = computed(() => store.product?.price ?? 0)
const stockQuantity = computed(() => store.product?.stockQuantity ?? 0)
const inStock = computed(() => stockQuantity.value > 0)

const errorMessage = computed(
  () => store.error?.detail || store.error?.title || 'Не удалось загрузить товар',
)

function currentId(): string {
  const raw = route.params.id
  return Array.isArray(raw) ? (raw[0] ?? '') : (raw ?? '')
}

function reload(): void {
  void store.fetch(currentId())
}

async function addToCart(): Promise<void> {
  const current = store.product
  if (current === null) {
    return
  }

  await cart.addItem(current.id, 1)

  if (cart.conflict) {
    message.warning('Корзина изменилась, обновите и попробуйте снова')
    return
  }

  if (cart.error) {
    message.error(cart.error.detail || cart.error.title || 'Не удалось добавить товар')
    return
  }

  message.success('Добавлено в корзину')
}

watch(
  () => route.params.id,
  () => {
    void store.fetch(currentId())
  },
  { immediate: true },
)
</script>

<style scoped>
.product__back {
  display: inline-block;
  margin-bottom: 16px;
}

.product__name {
  margin: 0 0 12px;
}

.product__description {
  margin: 0 0 20px;
}

.product__meta {
  display: flex;
  align-items: center;
  gap: 16px;
}

.product__actions {
  margin-top: 20px;
}

.product__price {
  font-size: 1.4rem;
  font-weight: 600;
}

.product__state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
}
</style>
