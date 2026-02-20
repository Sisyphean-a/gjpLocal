export interface LoginResponse {
  accessToken: string
  tokenType: string
  expiresAtUtc: string
}

export interface ProductLookupResponse {
  productName: string
  specification: string
  price: number
  barcodeMatchedBy: string
}

export interface ApiErrorResponse {
  code: string
  message: string
}
