<script setup lang="ts">
import type { ProductSearchItemResponse } from '../../../types/api'

defineProps<{
  items: ProductSearchItemResponse[]
}>()

const emit = defineEmits<{
  select: [barcode: string]
}>()
</script>

<template>
  <section class="scan-candidates">
    <p class="scan-candidates-title">找到 {{ items.length }} 条候选，请点选：</p>
    <button
      v-for="item in items"
      :key="`${item.matchedBy}-${item.barcode}`"
      type="button"
      class="scan-candidate-item"
      @click="emit('select', item.barcode)"
    >
      <span class="scan-candidate-name">{{ item.productName }}</span>
      <span class="scan-candidate-meta">
        编号：{{ item.productCode || '-' }} | 缩写码：{{ item.productShortCode || '-' }}
      </span>
      <span class="scan-candidate-meta">条码：{{ item.barcode }}</span>
      <span class="scan-candidate-price">￥{{ item.price.toFixed(2) }}</span>
    </button>
  </section>
</template>
