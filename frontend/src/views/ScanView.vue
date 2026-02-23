<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { isAxiosError } from 'axios'
import { Html5Qrcode, Html5QrcodeSupportedFormats } from 'html5-qrcode'
import { api } from '../services/api'
import { clearSession, getCurrentUsername } from '../services/auth'
import { playErrorTone, playSuccessTone } from '../services/sound'
import type {
  ApiErrorResponse,
  ProductLookupResponse,
  ProductSearchItemResponse,
  ProductSearchResponse,
} from '../types/api'

type LookupState = 'idle' | 'loading' | 'success' | 'error'

const router = useRouter()
const scannerElementId = 'barcode-reader'
const scanLogPrefix = '[barcode-scan]'
const scanErrorLogThrottleMs = 5000
const cameraStartTimeoutMs = 4000
const preferredFormats = [
  Html5QrcodeSupportedFormats.CODE_128,
  Html5QrcodeSupportedFormats.EAN_13,
  Html5QrcodeSupportedFormats.EAN_8,
  Html5QrcodeSupportedFormats.UPC_A,
  Html5QrcodeSupportedFormats.UPC_E,
  Html5QrcodeSupportedFormats.CODE_39,
]
const supportedFormats = [
  ...preferredFormats,
  Html5QrcodeSupportedFormats.CODE_93,
  Html5QrcodeSupportedFormats.ITF,
  Html5QrcodeSupportedFormats.CODABAR,
  Html5QrcodeSupportedFormats.UPC_EAN_EXTENSION,
  Html5QrcodeSupportedFormats.RSS_14,
  Html5QrcodeSupportedFormats.RSS_EXPANDED,
]

const username = ref(getCurrentUsername())
const state = ref<LookupState>('idle')
const statusText = ref('将商品条码放入扫描框内')
const currentBarcode = ref('')
const scannerReady = ref(false)
const scannerStarting = ref(false)
const manualKeyword = ref('')
const manualSearching = ref(false)
const manualCandidates = ref<ProductSearchItemResponse[]>([])
const product = ref<ProductLookupResponse | null>(null)
const errorMessage = ref('')
const showUnitDetails = ref(false)

let scanner: Html5Qrcode | null = null
let isProcessing = false
let lastScanErrorLogAt = 0

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

function createScannerConfig() {
  const readerElement = document.getElementById(scannerElementId)
  const viewportWidth = window.innerWidth || 360
  const baseWidth = readerElement?.clientWidth && readerElement.clientWidth > 0
    ? readerElement.clientWidth
    : viewportWidth - 28
  const scanBoxWidth = Math.min(540, Math.max(280, Math.floor(baseWidth - 6)))
  const scanBoxHeight = Math.max(150, Math.floor(scanBoxWidth * 0.56))

  return {
    fps: 6,
    qrbox: {
      width: scanBoxWidth,
      height: scanBoxHeight,
    },
    disableFlip: false,
  }
}

async function tryOptimizeRunningCamera(): Promise<void> {
  if (!scanner) {
    return
  }

  try {
    const capabilities = scanner.getRunningTrackCapabilities() as MediaTrackCapabilities & {
      focusMode?: string[]
      zoom?: { min?: number; max?: number }
    }

    const advanced: MediaTrackConstraintSet[] = []
    if (Array.isArray(capabilities.focusMode) && capabilities.focusMode.includes('continuous')) {
      advanced.push({ focusMode: 'continuous' } as MediaTrackConstraintSet)
    }

    if (capabilities.zoom && typeof capabilities.zoom.max === 'number') {
      const zoomMin = typeof capabilities.zoom.min === 'number' ? capabilities.zoom.min : 1
      const zoomMax = capabilities.zoom.max
      if (zoomMax > zoomMin) {
        const targetZoom = Math.min(2, zoomMax)
        advanced.push({ zoom: targetZoom } as MediaTrackConstraintSet)
      }
    }

    if (advanced.length > 0) {
      await scanner.applyVideoConstraints({ advanced })
      console.info(`${scanLogPrefix} camera constraints applied`, advanced)
    }
  } catch (error) {
    console.warn(`${scanLogPrefix} camera optimization skipped`, error)
  }
}

function withTimeout<T>(promise: Promise<T>, timeoutMs: number, label: string): Promise<T> {
  let timerId = 0
  const timeoutPromise = new Promise<never>((_, reject) => {
    timerId = window.setTimeout(() => {
      reject(new Error(`${label} timeout (${timeoutMs}ms)`))
    }, timeoutMs)
  })

  return Promise.race([promise, timeoutPromise]).finally(() => {
    if (timerId > 0) {
      window.clearTimeout(timerId)
    }
  })
}

function mapStartErrorMessage(error: unknown): string {
  const rawMessage = error instanceof Error ? error.message : `${error ?? 'unknown error'}`
  const lowerMessage = rawMessage.toLowerCase()

  if (lowerMessage.includes('notallowederror') || lowerMessage.includes('permission')) {
    return '摄像头权限被拒绝，请到浏览器设置里允许摄像头后重试。'
  }

  if (lowerMessage.includes('notfounderror') || lowerMessage.includes('device not found')) {
    return '未检测到可用摄像头，请确认设备摄像头可用。'
  }

  if (lowerMessage.includes('overconstrained') || lowerMessage.includes('constraint')) {
    return '摄像头参数不兼容，已自动降级但仍失败，请刷新页面重试。'
  }

  if (lowerMessage.includes('timeout')) {
    return '摄像头启动超时，请刷新页面重试。'
  }

  return `摄像头初始化失败：${rawMessage}`
}

function isNoCodeFoundMessage(message: string): boolean {
  const lowerMessage = message.toLowerCase()
  return (
    lowerMessage.includes('no multiformat readers were able to detect') ||
    lowerMessage.includes('notfoundexception')
  )
}

function onScanError(errorMessage: string): void {
  if (!errorMessage) {
    return
  }

  if (isNoCodeFoundMessage(errorMessage)) {
    return
  }

  const now = Date.now()
  if (now - lastScanErrorLogAt < scanErrorLogThrottleMs) {
    return
  }

  lastScanErrorLogAt = now
  console.warn(`${scanLogPrefix} decode warning:`, errorMessage)
}

async function startScanner(): Promise<void> {
  const config = createScannerConfig()
  const targetWidth = 2560
  const targetHeight = 1440
  const cameraProfiles: Array<{
    name: string
    source: string | MediaTrackConstraints
    videoConstraints?: MediaTrackConstraints
  }> = [
    {
      name: 'rear-hq-video-constraints',
      source: {
        facingMode: 'environment',
      },
      videoConstraints: {
        facingMode: 'environment',
        width: { ideal: targetWidth },
        height: { ideal: targetHeight },
      },
    },
    {
      name: 'rear-default',
      source: {
        facingMode: 'environment',
      },
    },
  ]

  const attempts: Array<{
    name: string
    source: string | MediaTrackConstraints
    videoConstraints?: MediaTrackConstraints
  }> = []
  try {
    const cameras = await withTimeout(Html5Qrcode.getCameras(), 3000, 'getCameras')
    const backCamera = cameras.find((camera) => /back|rear|environment|后置/i.test(camera.label))
    if (backCamera) {
      attempts.push({
        name: `camera-id-back:${backCamera.id}`,
        source: backCamera.id,
      })
    }

    const firstCamera = cameras[0]
    if (attempts.length === 0 && firstCamera) {
      attempts.push({
        name: `camera-id-first:${firstCamera.id}`,
        source: firstCamera.id,
      })
    }
  } catch (error) {
    console.warn(`${scanLogPrefix} getCameras failed`, error)
  }

  attempts.push(...cameraProfiles)

  let startError: unknown = null
  for (const attempt of attempts) {
    const trialScanner = new Html5Qrcode(scannerElementId, {
      verbose: false,
      formatsToSupport: supportedFormats,
    })

    scanner = trialScanner
    try {
      console.info(`${scanLogPrefix} trying source: ${attempt.name}`, attempt.source)
      const configForAttempt = attempt.videoConstraints
        ? { ...config, videoConstraints: attempt.videoConstraints }
        : config
      await withTimeout(
        trialScanner.start(attempt.source, configForAttempt, onScanSuccess, onScanError),
        cameraStartTimeoutMs,
        attempt.name,
      )
      scannerReady.value = true
      statusText.value = '扫描中...'
      await tryOptimizeRunningCamera()
      console.info(`${scanLogPrefix} scanner started with source: ${attempt.name}`)
      return
    } catch (error) {
      startError = error
      scanner = null
      console.warn(`${scanLogPrefix} source failed: ${attempt.name}`, error)
      try {
        await trialScanner.stop()
      } catch {
        // 失败分支下可能尚未进入 running，忽略 stop 异常。
      }
      try {
        await trialScanner.clear()
      } catch {
        // 清理失败不阻塞下一轮回退。
      }
    }
  }

  throw startError ?? new Error('摄像头初始化失败')
}

async function initializeScanner(forceRestart = false): Promise<void> {
  if (scannerStarting.value) {
    return
  }

  if (scannerReady.value && !forceRestart) {
    return
  }

  scannerStarting.value = true
  statusText.value = forceRestart ? '正在切换扫码模式...' : '正在初始化摄像头...'
  errorMessage.value = ''
  state.value = 'idle'

  if (forceRestart) {
    await stopScanner()
  }

  try {
    await startScanner()
  } catch (error) {
    console.error(`${scanLogPrefix} scanner start failed`, error)
    scannerReady.value = false
    state.value = 'error'
    statusText.value = '摄像头初始化失败'
    errorMessage.value = mapStartErrorMessage(error)
    await playErrorTone()
  } finally {
    scannerStarting.value = false
  }
}

function normalizeManualKeyword(value: string): string {
  return value.replace(/\s+/g, '').trim()
}

async function submitManualQuery(): Promise<void> {
  if (scannerStarting.value || manualSearching.value || state.value === 'loading') {
    return
  }

  const keyword = normalizeManualKeyword(manualKeyword.value)
  if (keyword.length < 2) {
    state.value = 'error'
    statusText.value = '查询失败'
    errorMessage.value = '请输入至少 2 位条码关键字。'
    await playErrorTone()
    return
  }

  if (isProcessing) {
    resetToIdle()
  }

  manualSearching.value = true
  manualCandidates.value = []
  state.value = 'loading'
  statusText.value = '正在查询...'
  errorMessage.value = ''
  currentBarcode.value = keyword

  try {
    const response = await api.get<ProductSearchResponse>('/api/products/search', {
      params: { keyword, limit: 20 },
    })

    const items = response.data.items ?? []
    if (items.length === 0) {
      state.value = 'error'
      statusText.value = '查询失败'
      errorMessage.value = '未找到匹配条码，请输入更多位数。'
      await playErrorTone()
      return
    }

    const firstItem = items[0]
    if (items.length === 1 && firstItem) {
      await onScanSuccess(firstItem.barcode)
      return
    }

    manualCandidates.value = items
    state.value = 'idle'
    statusText.value = `找到 ${items.length} 条候选，请点选`
    product.value = null
    showUnitDetails.value = false
    errorMessage.value = ''
    isProcessing = false
    try {
      scanner?.resume()
    } catch {
      // 不支持 resume 时忽略。
    }
  } catch (error) {
    console.error(`${scanLogPrefix} manual search failed`, error)
    state.value = 'error'
    statusText.value = '查询失败'
    if (isAxiosError<ApiErrorResponse>(error) && error.response?.data?.message) {
      errorMessage.value = error.response.data.message
    } else {
      errorMessage.value = '查询失败，请稍后重试。'
    }
    await playErrorTone()
  } finally {
    manualSearching.value = false
  }
}

async function selectManualCandidate(barcode: string): Promise<void> {
  if (manualSearching.value || scannerStarting.value || state.value === 'loading') {
    return
  }

  manualCandidates.value = []
  await onScanSuccess(barcode)
}

async function onScanSuccess(
  decodedText: string,
  decodedResult?: { result?: { format?: { formatName?: string } } },
): Promise<void> {
  if (isProcessing) {
    return
  }

  const barcode = decodedText.trim()
  if (barcode.length === 0) {
    return
  }

  const decodedFormatName = decodedResult?.result?.format?.formatName
  if (decodedFormatName) {
    console.info(`${scanLogPrefix} decoded format`, decodedFormatName)
  }

  isProcessing = true
  manualCandidates.value = []
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
    console.error(`${scanLogPrefix} lookup failed`, error)
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
  if (!scannerReady.value) {
    void initializeScanner()
    return
  }

  state.value = 'idle'
  statusText.value = scannerReady.value ? '扫描中...' : '正在初始化摄像头...'
  product.value = null
  errorMessage.value = ''
  currentBarcode.value = ''
  manualCandidates.value = []
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
    scannerReady.value = false
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
  scannerReady.value = false
}

onMounted(async () => {
  await initializeScanner()
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

    <section class="scan-manual-query">
      <input
        v-model="manualKeyword"
        class="scan-manual-input"
        type="text"
        maxlength="64"
        placeholder="输入完整条码或后六码"
        @keyup.enter="submitManualQuery"
      >
      <button
        type="button"
        class="scan-manual-submit"
        :disabled="manualSearching || scannerStarting || state === 'loading'"
        @click="submitManualQuery"
      >
        {{ manualSearching ? '查询中...' : '查询' }}
      </button>
    </section>

    <section v-if="manualCandidates.length > 1" class="scan-candidates">
      <p class="scan-candidates-title">找到 {{ manualCandidates.length }} 条候选，请点选：</p>
      <button
        v-for="item in manualCandidates"
        :key="`${item.barcodeMatchedBy}-${item.barcode}`"
        type="button"
        class="scan-candidate-item"
        @click="selectManualCandidate(item.barcode)"
      >
        <span class="scan-candidate-name">{{ item.productName }}</span>
        <span class="scan-candidate-meta">{{ item.barcode }}</span>
        <span class="scan-candidate-price">￥{{ item.price.toFixed(2) }}</span>
      </button>
    </section>

    <section class="scan-reader-shell">
      <div :id="scannerElementId" class="scan-reader" />
      <p class="scan-tip">{{ statusText }}</p>
      <button
        v-if="!scannerReady"
        type="button"
        class="scan-init-button"
        :disabled="scannerStarting"
        @click="() => initializeScanner()"
      >
        {{ scannerStarting ? '正在启动摄像头...' : '重试启用摄像头' }}
      </button>
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
