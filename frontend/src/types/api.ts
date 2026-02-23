export interface ApiEnvelope<T> {
  code: string
  message: string
  data: T | null
  traceId: string
  details?: unknown
}

export interface LoginResponse {
  accessToken: string
  tokenType: string
  expiresAtUtc: string
}

export interface ProductLookupResponse {
  productId: string
  productName: string
  productCode: string
  productShortCode: string
  specification: string
  price: number
  matchedBy: string
  pricingMeta: ProductPricingMetaResponse
  currentUnit: ProductLookupUnitResponse | null
  units: ProductLookupUnitResponse[]
}

export interface ProductPricingMetaResponse {
  sourceTable: string
  sourceField: string
  unitScoped: boolean
  priceTypeId: string | null
}

export interface ProductLookupUnitResponse {
  unitId: string
  unitName: string
  unitRate: string
  price: number
  barcodes: string[]
  isMatchedUnit: boolean
}

export interface ProductSearchResponse {
  keyword: string
  count: number
  items: ProductSearchItemResponse[]
}

export interface ProductSearchItemResponse {
  productName: string
  productCode: string
  productShortCode: string
  specification: string
  price: number
  barcode: string
  matchedBy: string
}

export interface ApiErrorResponse {
  code: string
  message: string
}
