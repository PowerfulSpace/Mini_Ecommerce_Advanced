<template>
  <div class="order-confirmation">
    <n-spin :show="true" />
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NSpin } from 'naive-ui'

// Checkout confirmation is a redirect stub (Phase 10.5 decision A): the order
// details screen is the single source of truth, so the dedicated confirmation
// route immediately forwards to /orders/:id instead of duplicating its UI.
const route = useRoute()
const router = useRouter()

onMounted(() => {
  const raw = route.params.id
  const id = Array.isArray(raw) ? (raw[0] ?? '') : (raw ?? '')
  void router.replace({ name: 'order-detail', params: { id } })
})
</script>

<style scoped>
.order-confirmation {
  display: flex;
  justify-content: center;
  padding: 40px 0;
}
</style>
