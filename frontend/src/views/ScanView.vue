<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { isAxiosError } from 'axios'
import { Html5Qrcode, Html5QrcodeSupportedFormats } from 'html5-qrcode'
import { api } from '../services/api'
import { clearSession, getCurrentUsername } from '../services/auth'
import { playErrorTone, playSuccessTone } from '../services/sound'
import type { ApiErrorResponse, ProductLookupResponse } from '../types/api'

type LookupState = 'idle' | 'loading' | 'success' | 'error'

const router = useRouter()
const scannerElementId = 'barcode-reader'
const supportedFormats = [
  Html5QrcodeSupportedFormats.CODE_128,
  Html5QrcodeSupportedFormats.EAN_13,
  Html5QrcodeSupportedFormats.EAN_8,
  Html5QrcodeSupportedFormats.UPC_A,
  Html5QrcodeSupportedFormats.UPC_E,
  Html5QrcodeSupportedFormats.CODE_39,
]

const username = ref(getCurrentUsername())
const state = ref<LookupState>('idle')
const statusText = ref('将商品条码放入扫描框内')
const currentBarcode = ref('')
const scannerReady = ref(false)
const product = ref<ProductLookupResponse | null>(null)
const errorMessage = ref('')
const showUnitDetails = ref(false)

let scanner: Html5Qrcode | null = null
let isProcessing = false

const pageClass = computed(() => ({
  'scan-page--error': state.value === 'error',
  'scan-page--success': state.value === 'success',
}))

const hasMultipleUnits = computed<boolean>(() => {
  if (!product.value) {
    return false
  }

  return product.value.units.length > 1
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

async function startScanner(): Promise<void> {
  scanner = new Html5Qrcode(scannerElementId, {
    verbose: false,
    formatsToSupport: supportedFormats,
  })

  const config = {
    fps: 12,
    qrbox: { width: 280, height: 150 },
    aspectRatio: 1.6,
    disableFlip: true,
  }

  try {
    await scanner.start(
      { facingMode: { exact: 'environment' } },
      config,
      onScanSuccess,
      () => undefined,
    )
  } catch {
    await scanner.start(
      { facingMode: 'environment' },
      config,
      onScanSuccess,
      () => undefined,
    )
  }

  scannerReady.value = true
  statusText.value = '扫描中...'
}

async function onScanSuccess(decodedText: string): Promise<void> {
  if (isProcessing) {
    return
  }

  const barcode = decodedText.trim()
  if (barcode.length === 0) {
    return
  }

  isProcessing = true
  currentBarcode.value = barcode
  state.value = 'loading'
  statusText.value = '正在查询...'
  showUnitDetails.value = false

  try {
    scanner?.pause(true)
  } catch {
    // 某些浏览器不支持 pause，忽略后继续流程。
  }

  try {
    const response = await api.get<ProductLookupResponse>('/api/products/lookup', {
      params: { barcode },
    })

    product.value = response.data
    errorMessage.value = ''
    state.value = 'success'
    statusText.value = '查询成功'
    await playSuccessTone()
  } catch (error) {
    product.value = null
    state.value = 'error'
    statusText.value = '查询失败'
    if (isAxiosError<ApiErrorResponse>(error) && error.response?.data?.message) {
      errorMessage.value = error.response.data.message
    } else {
      errorMessage.value = '网络异常，请检查主机服务和 WiFi 连接。'
    }
    await playErrorTone()
  }
}

function toggleUnitDetails(): void {
  showUnitDetails.value = !showUnitDetails.value
}

function resetToIdle(): void {
  state.value = 'idle'
  statusText.value = scannerReady.value ? '扫描中...' : '正在初始化摄像头...'
  product.value = null
  errorMessage.value = ''
  currentBarcode.value = ''
  showUnitDetails.value = false
  isProcessing = false

  try {
    scanner?.resume()
  } catch {
    // 不支持 resume 时，下一帧扫描依然可继续。
  }
}

async function logout(): Promise<void> {
  clearSession()
  await router.replace({ name: 'login' })
}

async function stopScanner(): Promise<void> {
  if (!scanner) {
    return
  }

  try {
    await scanner.stop()
  } catch {
    // 未处于扫描状态时 stop 可能抛错，忽略即可。
  }

  try {
    await scanner.clear()
  } catch {
    // 清理失败不阻塞页面卸载。
  }

  scanner = null
}

onMounted(async () => {
  statusText.value = '正在初始化摄像头...'
  try {
    await startScanner()
  } catch {
    state.value = 'error'
    statusText.value = '摄像头初始化失败'
    errorMessage.value = '请确认已通过 HTTPS 访问并允许摄像头权限。'
    await playErrorTone()
  }
})

onBeforeUnmount(async () => {
  await stopScanner()
})
</script>

<template>
  <main class="scan-page" :class="pageClass">
    <header class="scan-header">
      <div>
        <p class="scan-kicker">SWCS 实时查价</p>
        <p class="scan-user">当前账号：{{ username || '未登录' }}</p>
      </div>
      <button class="scan-logout" type="button" @click="logout">退出</button>
    </header>

    <section class="scan-reader-shell">
      <div :id="scannerElementId" class="scan-reader" />
      <p class="scan-tip">{{ statusText }}</p>
    </section>

    <section class="scan-instruction">
      <p>1. 条码对准扫描框中央。</p>
      <p>2. 扫描后自动查询，无需点击。</p>
      <p>3. 结果不会自动关闭，请点击“继续扫码”。</p>
    </section>

    <section v-if="state !== 'idle'" class="scan-overlay">
      <article class="scan-result" :class="`scan-result--${state}`">
        <template v-if="state === 'loading'">
          <p class="scan-result-label">查询中</p>
          <p class="scan-result-main">{{ currentBarcode }}</p>
        </template>

        <template v-else-if="state === 'success' && product">
          <p class="scan-result-label">查询成功</p>
          <p class="scan-product-name">{{ product.productName }}</p>
          <p class="scan-product-spec">{{ product.specification || '无规格' }}</p>
          <p class="scan-product-price">￥{{ product.price.toFixed(2) }}</p>
          <p class="scan-result-meta">匹配字段：{{ product.barcodeMatchedBy }}</p>

                    <button
            v-if="hasMultipleUnits"
            type="button"
            class="scan-units-toggle"
            @click="toggleUnitDetails"
          >
            {{ showUnitDetails ? '收起全部单位' : `展开全部单位（${product.units.length}）` }}
          </button>

          <section v-if="hasMultipleUnits && showUnitDetails" class="scan-unit-list">
            <article
              v-for="unit in product.units"
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
          <p class="scan-result-main">{{ currentBarcode }}</p>
          <p class="scan-result-error">{{ errorMessage }}</p>
        </template>

        <div class="scan-result-actions">
          <button type="button" class="scan-result-close" @click="resetToIdle">继续扫码</button>
        </div>
      </article>
    </section>
  </main>
</template>
