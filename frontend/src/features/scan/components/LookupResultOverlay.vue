<script setup lang="ts">
import { computed } from 'vue'
import type { ScanLookupResult, ScanUiState } from '../types/scan'

const props = defineProps<{
  state: ScanUiState
  result: ScanLookupResult | null
  currentBarcode: string
  showUnitDetails: boolean
  hasMultipleUnits: boolean
}>()

const emit = defineEmits<{
  reset: []
  toggleUnitDetails: []
}>()

const successResult = computed(() => {
  if (props.result?.kind !== 'success') {
    return null
  }

  return props.result
})

const errorResult = computed(() => {
  if (props.result?.kind !== 'error') {
    return null
  }

  return props.result
})

function formatRate(rate: string): string {
  const value = Number(rate)
  if (!Number.isFinite(value)) {
    return rate || '-'
  }

  if (Math.abs(value - Math.trunc(value)) < 1e-9) {
    return `${Math.trunc(value)}`
  }

  return value.toFixed(2)
}
</script>

<template>
  <section class="scan-overlay">
    <article class="scan-result" :class="`scan-result--${state}`">
      <template v-if="state === 'querying'">
        <p class="scan-result-label">查询中</p>
        <p class="scan-result-main">{{ currentBarcode }}</p>
      </template>

      <template v-else-if="state === 'showingResult' && successResult">
        <p class="scan-result-label">查询成功</p>
        <p class="scan-product-name">{{ successResult.product.productName }}</p>
        <p class="scan-product-spec">{{ successResult.product.specification || '无规格' }}</p>
        <p class="scan-product-price">￥{{ successResult.product.price.toFixed(2) }}</p>
        <p class="scan-result-meta">
          商品编号：{{ successResult.product.productCode || '-' }} |
          缩写码：{{ successResult.product.productShortCode || '-' }}
        </p>
        <p class="scan-result-meta">匹配字段：{{ successResult.product.matchedBy }}</p>

        <button
          v-if="hasMultipleUnits"
          type="button"
          class="scan-units-toggle"
          @click="emit('toggleUnitDetails')"
        >
          {{ showUnitDetails ? '收起全部单位' : `展开全部单位（${successResult.product.units.length}）` }}
        </button>

        <section v-if="hasMultipleUnits && showUnitDetails" class="scan-unit-list">
          <article
            v-for="unit in successResult.product.units"
            :key="unit.unitId"
            class="scan-unit-item"
            :class="{ 'scan-unit-item--matched': unit.isMatchedUnit }"
          >
            <p class="scan-unit-item-head">
              <span>{{ unit.unitName || '未命名单位' }}</span>
              <span>换算：{{ formatRate(unit.unitRate) }}</span>
              <span>￥{{ unit.price.toFixed(2) }}</span>
            </p>
            <p v-if="unit.barcodes.length > 0" class="scan-unit-barcodes">
              条码：{{ unit.barcodes.join(' / ') }}
            </p>
          </article>
        </section>
      </template>

      <template v-else>
        <p class="scan-result-label">查询失败</p>
        <p class="scan-result-main">{{ currentBarcode || errorResult?.barcode || '-' }}</p>
        <p class="scan-result-error">{{ errorResult?.message || '查询失败，请稍后重试。' }}</p>
      </template>

      <div class="scan-result-actions">
        <button type="button" class="scan-result-close" @click="emit('reset')">继续扫码</button>
      </div>
    </article>
  </section>
</template>
