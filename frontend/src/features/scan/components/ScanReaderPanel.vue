<script setup lang="ts">
defineProps<{
  elementId: string
  statusText: string
  scannerReady: boolean
  scannerStarting: boolean
}>()

const emit = defineEmits<{
  retry: []
}>()
</script>

<template>
  <section class="scan-reader-shell">
    <div :id="elementId" class="scan-reader" />
    <p class="scan-tip">{{ statusText }}</p>
    <button
      v-if="!scannerReady"
      type="button"
      class="scan-init-button"
      :disabled="scannerStarting"
      @click="emit('retry')"
    >
      {{ scannerStarting ? '正在启动摄像头...' : '重试启用摄像头' }}
    </button>
  </section>
</template>
