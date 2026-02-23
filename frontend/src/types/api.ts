export interface LoginResponse {
  accessToken: string
  tokenType: string
  expiresAtUtc: string
}

export interface ProductLookupResponse {
  productName: string
  productCode: string
  productShortCode: string
  specification: string
  price: number
  barcodeMatchedBy: string
  currentUnit: ProductLookupUnitResponse | null
  units: ProductLookupUnitResponse[]
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
  barcodeMatchedBy: string
}

export interface ApiErrorResponse {
  code: string
  message: string
}
