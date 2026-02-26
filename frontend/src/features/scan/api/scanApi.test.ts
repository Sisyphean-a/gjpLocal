import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ApiEnvelope, ProductLookupResponse, ProductSearchResponse } from '../../../types/api'

const getMock = vi.fn()

vi.mock('../../../services/api', () => ({
  api: {
    get: getMock,
  },
}))

describe('scanApi', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('returns lookup payload when response contains data', async () => {
    const payload = { productId: '1', productName: '可乐', productCode: 'A1', productShortCode: 'KL', specification: '', price: 3.5, matchedBy: 'barcode', pricingMeta: { sourceTable: 'p', sourceField: 'price', unitScoped: false, priceTypeId: null }, currentUnit: null, units: [] } satisfies ProductLookupResponse

    const envelope: ApiEnvelope<ProductLookupResponse> = {
      code: 'ok',
      message: '',
      data: payload,
      traceId: 'trace-1',
    }

    getMock.mockResolvedValue({ data: envelope })

    const { lookupProduct } = await import('./scanApi')
    await expect(lookupProduct('123')).resolves.toEqual(payload)
  })

  it('returns search items with empty fallback', async () => {
    const envelope: ApiEnvelope<ProductSearchResponse> = {
      code: 'ok',
      message: '',
      data: {
        keyword: '可乐',
        count: 1,
        items: [
          {
            productName: '可乐',
            productCode: 'A1',
            productShortCode: 'KL',
            specification: '',
            price: 3.5,
            barcode: '123',
            matchedBy: 'name',
          },
        ],
      },
      traceId: 'trace-2',
    }

    getMock.mockResolvedValue({ data: envelope })

    const { searchProducts } = await import('./scanApi')
    await expect(searchProducts('可乐')).resolves.toHaveLength(1)
  })

  it('maps axios style errors to backend message', async () => {
    const { mapApiErrorToMessage } = await import('./scanApi')
    const message = mapApiErrorToMessage(
      {
        isAxiosError: true,
        response: {
          data: {
            message: '后端返回错误',
          },
        },
      },
      'fallback',
    )

    expect(message).toBe('后端返回错误')
  })
})
