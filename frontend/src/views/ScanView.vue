<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import LookupResultOverlay from '../features/scan/components/LookupResultOverlay.vue'
import ManualCandidates from '../features/scan/components/ManualCandidates.vue'
import ScanReaderPanel from '../features/scan/components/ScanReaderPanel.vue'
import { useBarcodeScanner } from '../features/scan/composables/useBarcodeScanner'
import { useLookupFlow } from '../features/scan/composables/useLookupFlow'
import '../features/scan/scan.css'
import { clearSession, getCurrentUsername } from '../services/auth'
import { isAuthRequired } from '../services/sessionPolicy'

const router = useRouter()
const scannerElementId = 'barcode-reader'
const username = ref(getCurrentUsername())

let scannerController!: ReturnType<typeof useBarcodeScanner>

const lookupFlow = useLookupFlow({
  onPauseScanner: () => scannerController.pause(),
  onResumeScanner: () => scannerController.resume(),
})

scannerController = useBarcodeScanner({
  elementId: scannerElementId,
  onDecoded: lookupFlow.handleDecoded,
})

const {
  uiState,
  statusText,
  currentBarcode,
  manualKeyword,
  manualSearching,
  manualCandidates,
  result,
  showUnitDetails,
  hasOverlay,
  hasMultipleUnits,
  beginScannerInitialization,
  completeScannerInitialization,
  failScannerInitialization,
  markScannerStopped,
  submitManualQuery,
  selectManualCandidate,
  toggleUnitDetails,
  reset,
} = lookupFlow

const scannerReady = computed(() => scannerController.ready.value)
const scannerStarting = computed(() => scannerController.starting.value)
const manualSubmitDisabled = computed(() => {
  return (
    manualSearching.value ||
    scannerStarting.value ||
    uiState.value === 'querying' ||
    uiState.value === 'scannerStarting'
  )
})

const pageClass = computed(() => ({
  'scan-page--error': uiState.value === 'error',
  'scan-page--success': uiState.value === 'showingResult' && result.value?.kind === 'success',
}))

const showLogoutButton = computed(() => isAuthRequired() || Boolean(username.value))

async function initializeScanner(forceRestart = false): Promise<void> {
  beginScannerInitialization(forceRestart)
  try {
    await scannerController.initialize(forceRestart)
    completeScannerInitialization()
  } catch (error) {
    const message = error instanceof Error ? error.message : '摄像头初始化失败'
    await failScannerInitialization(message)
  }
}

async function logout(): Promise<void> {
  clearSession()
  username.value = ''

  if (isAuthRequired()) {
    await router.replace({ name: 'login' })
  }
}

onMounted(() => {
  void initializeScanner()
})

onBeforeUnmount(async () => {
  await scannerController.stop()
  markScannerStopped()
})
</script>

<template>
  <main class="scan-page" :class="pageClass">
    <header class="scan-header">
      <div>
        <p class="scan-kicker">SWCS 实时查价</p>
        <p class="scan-user">当前账号：{{ username || '未登录' }}</p>
      </div>
      <button v-if="showLogoutButton" class="scan-logout" type="button" @click="logout">退出</button>
    </header>

    <section class="scan-manual-query">
      <input
        v-model="manualKeyword"
        class="scan-manual-input"
        type="text"
        maxlength="64"
        placeholder="输入条码/商品全名/缩写码"
        @keyup.enter="submitManualQuery"
      >
      <button
        type="button"
        class="scan-manual-submit"
        :disabled="manualSubmitDisabled"
        @click="submitManualQuery"
      >
        {{ manualSearching ? '查询中...' : '查询' }}
      </button>
    </section>

    <ManualCandidates
      v-if="manualCandidates.length > 1"
      :items="manualCandidates"
      @select="selectManualCandidate"
    />

    <ScanReaderPanel
      :element-id="scannerElementId"
      :status-text="statusText"
      :scanner-ready="scannerReady"
      :scanner-starting="scannerStarting"
      @retry="initializeScanner"
    />

    <LookupResultOverlay
      v-if="hasOverlay"
      :state="uiState"
      :result="result"
      :current-barcode="currentBarcode"
      :show-unit-details="showUnitDetails"
      :has-multiple-units="hasMultipleUnits"
      @toggle-unit-details="toggleUnitDetails"
      @reset="reset"
    />
  </main>
</template>
