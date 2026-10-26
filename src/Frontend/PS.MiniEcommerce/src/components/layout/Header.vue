<template>
  <n-layout-header bordered>
    <div class="header-content" :class="{ 'header-content--mobile': isMobile }">
      <div class="header-left">
        <router-link to="/">
          <img :src="logo" alt="logo" class="logo" />
        </router-link>

        <n-menu
          v-if="!isMobile"
          mode="horizontal"
          :options="menuOptions"
          :value="active"
          @update:value="onSelect"
        />
      </div>

      <div v-if="isMobile" class="mobile-actions">
        <n-button
          quaternary
          circle
          class="burger-button"
          aria-label="Открыть меню"
          :aria-expanded="drawerOpen"
          aria-controls="mobile-nav-drawer"
          @click="drawerOpen = true"
        >
          ≡
        </n-button>
      </div>

      <div v-if="isDev && !isMobile" class="dev-identity">
        <n-text depth="3" class="dev-identity__label">dev identity</n-text>
        <n-select
          :value="authStore.testUser"
          :options="identityOptions"
          placeholder="select user"
          size="small"
          class="dev-identity__select"
          @update:value="onIdentityChange"
        />
      </div>
    </div>

    <n-drawer
      id="mobile-nav-drawer"
      v-model:show="drawerOpen"
      placement="left"
      width="280"
      :trap-focus="true"
      :auto-focus="true"
      :close-on-esc="true"
    >
      <n-drawer-content title="Меню" closable>
        <n-menu
          mode="vertical"
          :options="menuOptions"
          :value="active"
          @update:value="onSelect"
        />

        <div v-if="isDev && isMobile" class="dev-identity dev-identity--drawer">
          <n-text depth="3" class="dev-identity__label">dev identity</n-text>
          <n-select
            :value="authStore.testUser"
            :options="identityOptions"
            placeholder="select user"
            size="small"
            class="dev-identity__select"
            @update:value="onIdentityChange"
          />
        </div>
      </n-drawer-content>
    </n-drawer>
  </n-layout-header>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NDrawer, NDrawerContent, NLayoutHeader, NMenu, NSelect, NText } from 'naive-ui'
import type { MenuOption, SelectOption } from 'naive-ui'
import logo from '@/assets/icons/logo.svg'
import { useAuthStore } from '@/stores/auth'
import { useMediaQuery } from '@/composables/useMediaQuery'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()

// The identity selector is a development-only convenience, never a login.
const isDev = import.meta.env.DEV

const isMobile = useMediaQuery('(max-width: 767px)')
const drawerOpen = ref(false)

const active = computed(() => {
  const name = route.name as string | undefined
  if (name === 'order-detail') return 'orders'
  return name
})

const menuOptions: MenuOption[] = [
  { label: 'Catalog', key: 'catalog' },
  { label: 'Cart', key: 'cart' },
  { label: 'Orders', key: 'orders' },
]

const identityOptions: SelectOption[] = [
  { label: 'Alice', value: 'alice' },
  { label: 'Bob', value: 'bob' },
]

function onSelect(key: string) {
  drawerOpen.value = false
  router.push({ name: key })
}

function onIdentityChange(value: unknown) {
  if (value === 'alice' || value === 'bob') {
    authStore.setUser(value)
  } else {
    authStore.clear()
  }
}

watch(isMobile, (mobile) => {
  if (!mobile) {
    drawerOpen.value = false
  }
})

watch(
  () => route.fullPath,
  () => {
    drawerOpen.value = false
  },
)
</script>

<style scoped>
.header-content {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0 20px;
  height: 64px;
}

.header-content--mobile {
  padding: 0 12px;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 24px;
}

.logo {
  height: 32px;
  display: block;
}

.mobile-actions {
  display: flex;
  align-items: center;
}

.burger-button {
  font-size: 20px;
  line-height: 1;
}

.dev-identity {
  display: flex;
  align-items: center;
  gap: 8px;
}

.dev-identity__label {
  white-space: nowrap;
}

.dev-identity__select {
  width: 140px;
}

.dev-identity--drawer {
  margin-top: 24px;
}
</style>
