import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const scannerSpies = vi.hoisted(() => ({
  initialize: vi.fn().mockResolvedValue(undefined),
  stop: vi.fn().mockResolvedValue(undefined),
  pause: vi.fn(),
  resume: vi.fn(),
}))

const apiSpies = vi.hoisted(() => ({
  searchProducts: vi.fn(),
  lookupProduct: vi.fn(),
}))

const routerReplace = vi.hoisted(() => vi.fn())

vi.mock('vue-router', () => ({
  useRouter: () => ({
    replace: routerReplace,
  }),
}))

vi.mock('../services/auth', () => ({
  clearSession: vi.fn(),
  getCurrentUsername: () => 'user01',
}))

vi.mock('../services/sessionPolicy', () => ({
  isAuthRequired: () => true,
}))

vi.mock('../services/sound', () => ({
  playErrorTone: vi.fn().mockResolvedValue(undefined),
  playSuccessTone: vi.fn().mockResolvedValue(undefined),
}))

vi.mock('../features/scan/api/scanApi', () => ({
  searchProducts: apiSpies.searchProducts,
  lookupProduct: apiSpies.lookupProduct,
  mapApiErrorToMessage: (_error: unknown, fallbackMessage: string) => fallbackMessage,
}))

vi.mock('../features/scan/composables/useBarcodeScanner', async () => {
  const { ref } = await import('vue')
  const ready = ref(false)
  const starting = ref(false)

  scannerSpies.initialize.mockImplementation(async () => {
    ready.value = true
  })
  scannerSpies.stop.mockImplementation(async () => {
    ready.value = false
  })

  return {
    useBarcodeScanner: () => ({
      ready,
      starting,
      initialize: scannerSpies.initialize,
      stop: scannerSpies.stop,
      pause: scannerSpies.pause,
      resume: scannerSpies.resume,
    }),
  }
})

describe('ScanView', () => {
  beforeEach(() => {
    scannerSpies.initialize.mockClear()
    scannerSpies.pause.mockClear()
    scannerSpies.resume.mockClear()
    apiSpies.searchProducts.mockReset()
    apiSpies.lookupProduct.mockReset()
  })

  it('initializes scanner when mounted', async () => {
    const ScanView = (await import('./ScanView.vue')).default
    mount(ScanView)

    expect(scannerSpies.initialize).toHaveBeenCalledTimes(1)
  })

  it('shows manual candidates and uses candidate barcode for lookup', async () => {
    apiSpies.searchProducts.mockResolvedValue([
      {
        productName: '可乐',
        productCode: 'A1',
        productShortCode: 'KL',
        specification: '',
        price: 3.5,
        barcode: '123',
        matchedBy: 'name',
      },
      {
        productName: '雪碧',
        productCode: 'A2',
        productShortCode: 'XB',
        specification: '',
        price: 3.5,
        barcode: '456',
        matchedBy: 'name',
      },
    ])
    apiSpies.lookupProduct.mockResolvedValue({
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

    const ScanView = (await import('./ScanView.vue')).default
    const wrapper = mount(ScanView)

    await wrapper.find('.scan-manual-input').setValue('可乐')
    await wrapper.find('.scan-manual-submit').trigger('click')
    await vi.waitFor(() => {
      expect(wrapper.findAll('.scan-candidate-item')).toHaveLength(2)
    })

    const candidateButtons = wrapper.findAll('.scan-candidate-item')
    expect(candidateButtons).toHaveLength(2)
    await candidateButtons[0]!.trigger('click')
    expect(apiSpies.lookupProduct).toHaveBeenCalledWith('123')
  })
})
