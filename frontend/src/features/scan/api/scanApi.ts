import { isAxiosError } from 'axios'
import { api } from '../../../services/api'
import type {
  ApiEnvelope,
  ProductLookupResponse,
  ProductSearchItemResponse,
  ProductSearchResponse,
} from '../../../types/api'

export async function lookupProduct(barcode: string): Promise<ProductLookupResponse> {
  const response = await api.get<ApiEnvelope<ProductLookupResponse>>('/api/v2/products/lookup', {
    params: { barcode },
  })

  if (!response.data.data) {
    throw new Error('lookup payload missing data')
  }

  return response.data.data
}

export async function searchProducts(keyword: string, limit = 20): Promise<ProductSearchItemResponse[]> {
  const response = await api.get<ApiEnvelope<ProductSearchResponse>>('/api/v2/products/search', {
    params: { keyword, limit },
  })

  return response.data.data?.items ?? []
}

export function mapApiErrorToMessage(error: unknown, fallbackMessage: string): string {
  if (isAxiosError<ApiEnvelope<unknown>>(error) && error.response?.data?.message) {
    return error.response.data.message
  }

  return fallbackMessage
}
