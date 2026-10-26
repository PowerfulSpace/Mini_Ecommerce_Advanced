<template>
  <div class="layout">
    <Header />

    <n-layout-content content-style="padding: 20px;">
      <router-view />
    </n-layout-content>

    <Footer />
  </div>
</template>

<script setup lang="ts">
import { NLayoutContent } from 'naive-ui'
import Header from '@/components/layout/Header.vue'
import Footer from '@/components/layout/Footer.vue'
</script>

<style scoped>
.layout {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
}
</style>
