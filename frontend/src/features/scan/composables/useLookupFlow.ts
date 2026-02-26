import { computed, ref } from 'vue'
import { playErrorTone, playSuccessTone } from '../../../services/sound'
import type { ProductLookupResponse, ProductSearchItemResponse } from '../../../types/api'
import { lookupProduct as lookupProductApi, mapApiErrorToMessage, searchProducts as searchProductsApi } from '../api/scanApi'
import type { ScanLookupResult, ScanStateEvent, ScanUiState, ScannerDecodeResult } from '../types/scan'

export interface UseLookupFlowOptions {
  onPauseScanner: () => void
  onResumeScanner: () => void
  lookupProduct?: (barcode: string) => Promise<ProductLookupResponse>
  searchProducts?: (keyword: string, limit?: number) => Promise<ProductSearchItemResponse[]>
  playSuccessTone?: () => Promise<void>
  playErrorTone?: () => Promise<void>
}

const idleStatusText = '将商品条码放入扫描框内'

export function useLookupFlow(options: UseLookupFlowOptions) {
  const lookupProduct = options.lookupProduct ?? lookupProductApi
  const searchProducts = options.searchProducts ?? searchProductsApi
  const playSuccess = options.playSuccessTone ?? playSuccessTone
  const playError = options.playErrorTone ?? playErrorTone

  const uiState = ref<ScanUiState>('idle')
  const scannerReady = ref(false)
  const statusText = ref(idleStatusText)
  const currentBarcode = ref('')
  const manualKeyword = ref('')
  const manualSearching = ref(false)
  const manualCandidates = ref<ProductSearchItemResponse[]>([])
  const result = ref<ScanLookupResult | null>(null)
  const showUnitDetails = ref(false)

  const hasOverlay = computed(() => {
    return uiState.value === 'querying' || uiState.value === 'showingResult' || uiState.value === 'error'
  })

  const hasMultipleUnits = computed(() => {
    if (result.value?.kind !== 'success') {
      return false
    }

    return result.value.product.units.length > 1
  })

  let queryInFlight = false

  function dispatch(event: ScanStateEvent): void {
    switch (event) {
      case 'INIT_SCANNER':
        uiState.value = 'scannerStarting'
        return
      case 'SCANNER_READY':
        uiState.value = 'scanning'
        return
      case 'SCAN_SUCCESS':
      case 'MANUAL_QUERY':
        uiState.value = 'querying'
        return
      case 'QUERY_OK':
        uiState.value = 'showingResult'
        return
      case 'QUERY_FAIL':
        uiState.value = 'error'
        return
      case 'RESET':
        uiState.value = scannerReady.value ? 'scanning' : 'idle'
        return
      default:
        return
    }
  }

  function setErrorResult(message: string, barcode = currentBarcode.value): void {
    result.value = {
      kind: 'error',
      barcode,
      message,
    }
    dispatch('QUERY_FAIL')
    statusText.value = '查询失败'
  }

  function beginScannerInitialization(forceRestart = false): void {
    dispatch('INIT_SCANNER')
    statusText.value = forceRestart ? '正在切换扫码模式...' : '正在初始化摄像头...'
    result.value = null
    manualCandidates.value = []
    showUnitDetails.value = false
  }

  function completeScannerInitialization(): void {
    scannerReady.value = true
    dispatch('SCANNER_READY')
    statusText.value = '扫描中...'
  }

  async function failScannerInitialization(message: string): Promise<void> {
    scannerReady.value = false
    currentBarcode.value = ''
    manualCandidates.value = []
    setErrorResult(message, '')
    await playError()
  }

  function markScannerStopped(): void {
    scannerReady.value = false
    if (uiState.value === 'scanning' || uiState.value === 'scannerStarting') {
      dispatch('RESET')
      statusText.value = idleStatusText
    }
  }

  async function lookupByBarcode(barcode: string, event: 'SCAN_SUCCESS' | 'MANUAL_QUERY'): Promise<void> {
    const normalizedBarcode = barcode.trim()
    if (!normalizedBarcode || queryInFlight) {
      return
    }

    queryInFlight = true
    dispatch(event)
    currentBarcode.value = normalizedBarcode
    manualCandidates.value = []
    result.value = null
    showUnitDetails.value = false
    statusText.value = '正在查询...'

    options.onPauseScanner()

    try {
      const product = await lookupProduct(normalizedBarcode)
      result.value = {
        kind: 'success',
        barcode: normalizedBarcode,
        product,
      }
      dispatch('QUERY_OK')
      statusText.value = '查询成功'
      await playSuccess()
    } catch (error) {
      const message = mapApiErrorToMessage(error, '网络异常，请检查主机服务和 WiFi 连接。')
      setErrorResult(message, normalizedBarcode)
      await playError()
    } finally {
      queryInFlight = false
    }
  }

  async function handleDecoded(decodedText: string, decodedResult?: ScannerDecodeResult): Promise<void> {
    const decodedFormatName = decodedResult?.result?.format?.formatName
    if (decodedFormatName) {
      console.info('[barcode-scan] decoded format', decodedFormatName)
    }

    await lookupByBarcode(decodedText, 'SCAN_SUCCESS')
  }

  function normalizeManualKeyword(value: string): string {
    return value.trim()
  }

  async function submitManualQuery(): Promise<void> {
    if (uiState.value === 'scannerStarting' || uiState.value === 'querying' || manualSearching.value) {
      return
    }

    const keyword = normalizeManualKeyword(manualKeyword.value)
    if (keyword.length < 2) {
      currentBarcode.value = keyword
      setErrorResult('请输入至少 2 位关键字（条码/商品全名/缩写码）。', keyword)
      await playError()
      return
    }

    manualSearching.value = true
    dispatch('MANUAL_QUERY')
    currentBarcode.value = keyword
    manualCandidates.value = []
    result.value = null
    statusText.value = '正在查询...'

    try {
      const items = await searchProducts(keyword, 20)
      if (items.length === 0) {
        setErrorResult('未找到匹配商品，请尝试条码、商品全名或缩写码。', keyword)
        await playError()
        return
      }

      const firstItem = items[0]
      if (items.length === 1 && firstItem) {
        manualSearching.value = false
        await lookupByBarcode(firstItem.barcode, 'MANUAL_QUERY')
        return
      }

      manualCandidates.value = items
      dispatch('RESET')
      statusText.value = `找到 ${items.length} 条候选，请点选`
      options.onResumeScanner()
    } catch (error) {
      const message = mapApiErrorToMessage(error, '查询失败，请稍后重试。')
      setErrorResult(message, keyword)
      await playError()
    } finally {
      manualSearching.value = false
    }
  }

  async function selectManualCandidate(barcode: string): Promise<void> {
    if (manualSearching.value || uiState.value === 'scannerStarting' || uiState.value === 'querying') {
      return
    }

    manualCandidates.value = []
    await lookupByBarcode(barcode, 'MANUAL_QUERY')
  }

  function toggleUnitDetails(): void {
    showUnitDetails.value = !showUnitDetails.value
  }

  function reset(): void {
    dispatch('RESET')
    statusText.value = scannerReady.value ? '扫描中...' : idleStatusText
    result.value = null
    currentBarcode.value = ''
    manualCandidates.value = []
    showUnitDetails.value = false
    queryInFlight = false

    if (scannerReady.value) {
      options.onResumeScanner()
    }
  }

  return {
    uiState,
    scannerReady,
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
    handleDecoded,
    submitManualQuery,
    selectManualCandidate,
    toggleUnitDetails,
    reset,
  }
}
