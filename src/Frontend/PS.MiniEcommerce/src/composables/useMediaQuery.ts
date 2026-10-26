import { onUnmounted, ref, type Ref } from 'vue'

export function useMediaQuery(query: string): Ref<boolean> {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return ref(false)
  }

  const mediaQueryList = window.matchMedia(query)
  const matches = ref(mediaQueryList.matches)

  const onChange = (event: MediaQueryListEvent) => {
    matches.value = event.matches
  }

  mediaQueryList.addEventListener('change', onChange)

  onUnmounted(() => {
    mediaQueryList.removeEventListener('change', onChange)
  })

  return matches
}
