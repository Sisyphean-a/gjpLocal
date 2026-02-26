import { describe, expect, it, vi } from 'vitest'
import { useLookupFlow } from './useLookupFlow'

describe('useLookupFlow', () => {
  it('enters scannerStarting then scanning on scanner lifecycle', () => {
    const flow = useLookupFlow({
      onPauseScanner: vi.fn(),
      onResumeScanner: vi.fn(),
      playErrorTone: vi.fn().mockResolvedValue(undefined),
      playSuccessTone: vi.fn().mockResolvedValue(undefined),
      lookupProduct: vi.fn(),
      searchProducts: vi.fn(),
    })

    flow.beginScannerInitialization()
    expect(flow.uiState.value).toBe('scannerStarting')

    flow.completeScannerInitialization()
    expect(flow.uiState.value).toBe('scanning')
    expect(flow.statusText.value).toBe('扫描中...')
  })

  it('shows error state when manual keyword is too short', async () => {
    const playErrorTone = vi.fn().mockResolvedValue(undefined)
    const flow = useLookupFlow({
      onPauseScanner: vi.fn(),
      onResumeScanner: vi.fn(),
      playErrorTone,
      playSuccessTone: vi.fn().mockResolvedValue(undefined),
      lookupProduct: vi.fn(),
      searchProducts: vi.fn(),
    })

    flow.manualKeyword.value = 'a'
    await flow.submitManualQuery()

    expect(flow.uiState.value).toBe('error')
    expect(flow.result.value?.kind).toBe('error')
    expect(playErrorTone).toHaveBeenCalledTimes(1)
  })

  it('queries lookup directly when manual search has single candidate', async () => {
    const lookupProduct = vi.fn().mockResolvedValue({
      productId: '1',
      productName: '可乐',
      productCode: 'A1',
      productShortCode: 'KL',
      specification: '',
      price: 3.5,
      matchedBy: 'barcode',
      pricingMeta: {
        sourceTable: 'p',
        sourceField: 'price',
        unitScoped: false,
        priceTypeId: null,
      },
      currentUnit: null,
      units: [],
    })
    const flow = useLookupFlow({
      onPauseScanner: vi.fn(),
      onResumeScanner: vi.fn(),
      playErrorTone: vi.fn().mockResolvedValue(undefined),
      playSuccessTone: vi.fn().mockResolvedValue(undefined),
      lookupProduct,
      searchProducts: vi.fn().mockResolvedValue([
        {
          productName: '可乐',
          productCode: 'A1',
          productShortCode: 'KL',
          specification: '',
          price: 3.5,
          barcode: '123456',
          matchedBy: 'name',
        },
      ]),
    })

    flow.completeScannerInitialization()
    flow.manualKeyword.value = '可乐'
    await flow.submitManualQuery()

    expect(lookupProduct).toHaveBeenCalledWith('123456')
    expect(flow.uiState.value).toBe('showingResult')
    expect(flow.result.value?.kind).toBe('success')
  })

  it('resets back to scanning when scanner is ready', () => {
    const onResumeScanner = vi.fn()
    const flow = useLookupFlow({
      onPauseScanner: vi.fn(),
      onResumeScanner,
      playErrorTone: vi.fn().mockResolvedValue(undefined),
      playSuccessTone: vi.fn().mockResolvedValue(undefined),
      lookupProduct: vi.fn(),
      searchProducts: vi.fn(),
    })

    flow.completeScannerInitialization()
    flow.reset()

    expect(flow.uiState.value).toBe('scanning')
    expect(flow.statusText.value).toBe('扫描中...')
    expect(onResumeScanner).toHaveBeenCalledTimes(1)
  })
})
