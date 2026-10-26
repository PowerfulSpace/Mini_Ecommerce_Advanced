<template>
  <n-layout-footer bordered class="footer">
    <n-text depth="3"> © {{ year }} My App </n-text>
  </n-layout-footer>
</template>

<script setup lang="ts">
import { NLayoutFooter, NText } from 'naive-ui'

const year = new Date().getFullYear()
</script>

<style scoped>
.footer {
  text-align: center;
  padding: 15px 20px;
}
</style>
