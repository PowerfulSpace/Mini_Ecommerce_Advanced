<template>
  <div class="catalog">
    <h2>Catalog</h2>

    <n-spin :show="store.loading">
      <div v-if="store.error" class="catalog__state">
        <n-alert type="error">{{ errorMessage }}</n-alert>
        <n-button type="primary" @click="reload">Повторить</n-button>
      </div>

      <n-empty v-else-if="store.isEmpty" class="catalog__state" description="Каталог пуст" />

      <div v-else class="catalog__grid">
        <router-link
          v-for="product in store.items"
          :key="product.id"
          :to="{ name: 'product', params: { id: product.id } }"
          class="catalog__card-link"
        >
          <n-card hoverable>
            <h3 class="product-card__name">{{ product.name }}</h3>
            <p class="product-card__description">{{ product.description }}</p>
            <div class="product-card__meta">
              <span class="product-card__price">{{ formatPrice(product.price) }}</span>
              <n-tag size="small" :type="product.stockQuantity > 0 ? 'success' : 'warning'">
                Остаток: {{ product.stockQuantity }}
              </n-tag>
            </div>
          </n-card>
        </router-link>
      </div>

      <div v-if="showPagination" class="catalog__pagination">
        <n-pagination
          :page="store.page"
          :page-size="store.pageSize"
          :item-count="store.totalCount"
          :disabled="store.loading"
          @update:page="onPageChange"
        />
      </div>
    </n-spin>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { NAlert, NButton, NCard, NEmpty, NPagination, NSpin, NTag } from 'naive-ui'
import { useCatalogStore } from '@/stores/catalog'

const store = useCatalogStore()

const usdFormatter = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

function formatPrice(value: number): string {
  return usdFormatter.format(value)
}

const errorMessage = computed(
  () => store.error?.detail || store.error?.title || 'Не удалось загрузить каталог',
)

const showPagination = computed(() => !store.error && store.totalCount > 0)

function onPageChange(target: number): void {
  void store.fetchPage(target)
}

function reload(): void {
  void store.fetchPage(store.page)
}

onMounted(() => {
  // The catalog store is application-scoped, so returning from a product page
  // must not reset pagination: reload the page the user was already on.
  void store.fetchPage(store.page)
})
</script>

<style scoped>
.catalog__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 16px;
}

.catalog__card-link {
  display: block;
  color: inherit;
  text-decoration: none;
}

.product-card__name {
  margin: 0 0 8px;
}

.product-card__description {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  margin: 0 0 16px;
  color: var(--n-text-color-3, #666);
}

.product-card__meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.product-card__price {
  font-size: 1.1rem;
  font-weight: 600;
}

.catalog__state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
}

.catalog__pagination {
  display: flex;
  justify-content: center;
  margin-top: 24px;
}
</style>
