import type { ProductLookupResponse } from '../../../types/api'

export type ScanUiState =
  | 'idle'
  | 'scannerStarting'
  | 'scanning'
  | 'querying'
  | 'showingResult'
  | 'error'

export type ScanStateEvent =
  | 'INIT_SCANNER'
  | 'SCANNER_READY'
  | 'SCAN_SUCCESS'
  | 'MANUAL_QUERY'
  | 'QUERY_OK'
  | 'QUERY_FAIL'
  | 'RESET'

export interface ScannerDecodeResult {
  result?: {
    format?: {
      formatName?: string
    }
  }
}

export interface ScanSuccessResult {
  kind: 'success'
  barcode: string
  product: ProductLookupResponse
}

export interface ScanErrorResult {
  kind: 'error'
  barcode: string
  message: string
}

export type ScanLookupResult = ScanSuccessResult | ScanErrorResult
