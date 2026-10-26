import { createRouter, createWebHistory } from 'vue-router'

// Phase 9 router skeleton. Screen behavior is implemented in Phase 10.
const routes = [
  {
    path: '/',
    name: 'catalog',
    component: () => import('@/views/CatalogView.vue'),
  },
  {
    path: '/products/:id',
    name: 'product',
    component: () => import('@/views/ProductView.vue'),
  },
  {
    path: '/cart',
    name: 'cart',
    component: () => import('@/views/CartView.vue'),
  },
  {
    path: '/orders',
    name: 'orders',
    component: () => import('@/views/OrdersView.vue'),
  },
  {
    path: '/orders/:id',
    name: 'order-detail',
    component: () => import('@/views/OrderDetailView.vue'),
  },
  {
    path: '/checkout/success/:id',
    name: 'checkout-success',
    component: () => import('@/views/OrderConfirmationView.vue'),
  },
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
})

export default router
