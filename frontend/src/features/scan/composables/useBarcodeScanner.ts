import { ref } from 'vue'
import type {
  CameraDevice,
  Html5Qrcode,
  Html5QrcodeCameraScanConfig,
  Html5QrcodeSupportedFormats,
} from 'html5-qrcode'
import type { ScannerDecodeResult } from '../types/scan'

type Html5QrcodeModule = typeof import('html5-qrcode')

interface CameraAttempt {
  name: string
  source: string | MediaTrackConstraints
  videoConstraints?: MediaTrackConstraints
}

export interface UseBarcodeScannerOptions {
  elementId: string
  onDecoded: (decodedText: string, decodedResult?: ScannerDecodeResult) => void | Promise<void>
  scanLogPrefix?: string
  scanErrorLogThrottleMs?: number
  cameraStartTimeoutMs?: number
}

const defaultScanLogPrefix = '[barcode-scan]'
const defaultScanErrorLogThrottleMs = 5000
const defaultCameraStartTimeoutMs = 4000

export function useBarcodeScanner(options: UseBarcodeScannerOptions) {
  const ready = ref(false)
  const starting = ref(false)

  const scanLogPrefix = options.scanLogPrefix ?? defaultScanLogPrefix
  const scanErrorLogThrottleMs = options.scanErrorLogThrottleMs ?? defaultScanErrorLogThrottleMs
  const cameraStartTimeoutMs = options.cameraStartTimeoutMs ?? defaultCameraStartTimeoutMs

  let scannerModule: Html5QrcodeModule | null = null
  let scanner: Html5Qrcode | null = null
  let lastScanErrorLogAt = 0

  async function loadScannerModule(): Promise<Html5QrcodeModule> {
    if (!scannerModule) {
      scannerModule = await import('html5-qrcode')
    }

    return scannerModule
  }

  function buildSupportedFormats(module: Html5QrcodeModule): Html5QrcodeSupportedFormats[] {
    const preferredFormats = [
      module.Html5QrcodeSupportedFormats.CODE_128,
      module.Html5QrcodeSupportedFormats.EAN_13,
      module.Html5QrcodeSupportedFormats.EAN_8,
      module.Html5QrcodeSupportedFormats.UPC_A,
      module.Html5QrcodeSupportedFormats.UPC_E,
      module.Html5QrcodeSupportedFormats.CODE_39,
    ]

    return [
      ...preferredFormats,
      module.Html5QrcodeSupportedFormats.CODE_93,
      module.Html5QrcodeSupportedFormats.ITF,
      module.Html5QrcodeSupportedFormats.CODABAR,
      module.Html5QrcodeSupportedFormats.UPC_EAN_EXTENSION,
      module.Html5QrcodeSupportedFormats.RSS_14,
      module.Html5QrcodeSupportedFormats.RSS_EXPANDED,
    ]
  }

  function createScannerConfig(): Html5QrcodeCameraScanConfig {
    const readerElement = document.getElementById(options.elementId)
    const viewportWidth = window.innerWidth || 360
    const baseWidth = readerElement?.clientWidth && readerElement.clientWidth > 0
      ? readerElement.clientWidth
      : viewportWidth - 28
    const scanBoxWidth = Math.min(540, Math.max(280, Math.floor(baseWidth - 6)))
    const scanBoxHeight = Math.max(150, Math.floor(scanBoxWidth * 0.56))

    return {
      fps: 6,
      qrbox: {
        width: scanBoxWidth,
        height: scanBoxHeight,
      },
      disableFlip: false,
    }
  }

  function withTimeout<T>(promise: Promise<T>, timeoutMs: number, label: string): Promise<T> {
    let timerId = 0
    const timeoutPromise = new Promise<never>((_, reject) => {
      timerId = window.setTimeout(() => {
        reject(new Error(`${label} timeout (${timeoutMs}ms)`))
      }, timeoutMs)
    })

    return Promise.race([promise, timeoutPromise]).finally(() => {
      if (timerId > 0) {
        window.clearTimeout(timerId)
      }
    })
  }

  function mapStartErrorMessage(error: unknown): string {
    const rawMessage = error instanceof Error ? error.message : `${error ?? 'unknown error'}`
    const lowerMessage = rawMessage.toLowerCase()

    if (lowerMessage.includes('notallowederror') || lowerMessage.includes('permission')) {
      return '摄像头权限被拒绝，请到浏览器设置里允许摄像头后重试。'
    }

    if (lowerMessage.includes('notfounderror') || lowerMessage.includes('device not found')) {
      return '未检测到可用摄像头，请确认设备摄像头可用。'
    }

    if (lowerMessage.includes('overconstrained') || lowerMessage.includes('constraint')) {
      return '摄像头参数不兼容，已自动降级但仍失败，请刷新页面重试。'
    }

    if (lowerMessage.includes('timeout')) {
      return '摄像头启动超时，请刷新页面重试。'
    }

    return `摄像头初始化失败：${rawMessage}`
  }

  function isNoCodeFoundMessage(message: string): boolean {
    const lowerMessage = message.toLowerCase()
    return (
      lowerMessage.includes('no multiformat readers were able to detect') ||
      lowerMessage.includes('notfoundexception')
    )
  }

  function onScanError(errorMessage: string): void {
    if (!errorMessage || isNoCodeFoundMessage(errorMessage)) {
      return
    }

    const now = Date.now()
    if (now - lastScanErrorLogAt < scanErrorLogThrottleMs) {
      return
    }

    lastScanErrorLogAt = now
    console.warn(`${scanLogPrefix} decode warning:`, errorMessage)
  }

  async function tryOptimizeRunningCamera(activeScanner: Html5Qrcode): Promise<void> {
    try {
      const capabilities = activeScanner.getRunningTrackCapabilities() as MediaTrackCapabilities & {
        focusMode?: string[]
        zoom?: { min?: number; max?: number }
      }

      const advanced: MediaTrackConstraintSet[] = []
      if (Array.isArray(capabilities.focusMode) && capabilities.focusMode.includes('continuous')) {
        advanced.push({ focusMode: 'continuous' } as MediaTrackConstraintSet)
      }

      if (capabilities.zoom && typeof capabilities.zoom.max === 'number') {
        const zoomMin = typeof capabilities.zoom.min === 'number' ? capabilities.zoom.min : 1
        const zoomMax = capabilities.zoom.max
        if (zoomMax > zoomMin) {
          const targetZoom = Math.min(2, zoomMax)
          advanced.push({ zoom: targetZoom } as MediaTrackConstraintSet)
        }
      }

      if (advanced.length > 0) {
        await activeScanner.applyVideoConstraints({ advanced })
        console.info(`${scanLogPrefix} camera constraints applied`, advanced)
      }
    } catch (error) {
      console.warn(`${scanLogPrefix} camera optimization skipped`, error)
    }
  }

  async function getCameraAttempts(module: Html5QrcodeModule): Promise<CameraAttempt[]> {
    const attempts: CameraAttempt[] = []
    const targetWidth = 2560
    const targetHeight = 1440

    try {
      const cameras = await withTimeout(module.Html5Qrcode.getCameras(), 3000, 'getCameras')
      const backCamera = cameras.find((camera: CameraDevice) =>
        /back|rear|environment|后置/i.test(camera.label),
      )

      if (backCamera) {
        attempts.push({
          name: `camera-id-back:${backCamera.id}`,
          source: backCamera.id,
        })
      }

      const firstCamera = cameras[0]
      if (attempts.length === 0 && firstCamera) {
        attempts.push({
          name: `camera-id-first:${firstCamera.id}`,
          source: firstCamera.id,
        })
      }
    } catch (error) {
      console.warn(`${scanLogPrefix} getCameras failed`, error)
    }

    attempts.push(
      {
        name: 'rear-hq-video-constraints',
        source: { facingMode: 'environment' },
        videoConstraints: {
          facingMode: 'environment',
          width: { ideal: targetWidth },
          height: { ideal: targetHeight },
        },
      },
      {
        name: 'rear-default',
        source: { facingMode: 'environment' },
      },
    )

    return attempts
  }

  async function startScanner(): Promise<void> {
    const module = await loadScannerModule()
    const config = createScannerConfig()
    const attempts = await getCameraAttempts(module)
    const formatsToSupport = buildSupportedFormats(module)

    let startError: unknown = null

    for (const attempt of attempts) {
      const trialScanner = new module.Html5Qrcode(options.elementId, {
        verbose: false,
        formatsToSupport,
      })

      scanner = trialScanner
      try {
        console.info(`${scanLogPrefix} trying source: ${attempt.name}`, attempt.source)
        const configForAttempt = attempt.videoConstraints
          ? { ...config, videoConstraints: attempt.videoConstraints }
          : config

        await withTimeout(
          trialScanner.start(
            attempt.source,
            configForAttempt,
            (decodedText: string, decodedResult: ScannerDecodeResult) => {
              void options.onDecoded(decodedText, decodedResult)
            },
            onScanError,
          ),
          cameraStartTimeoutMs,
          attempt.name,
        )

        ready.value = true
        await tryOptimizeRunningCamera(trialScanner)
        console.info(`${scanLogPrefix} scanner started with source: ${attempt.name}`)
        return
      } catch (error) {
        startError = error
        scanner = null
        console.warn(`${scanLogPrefix} source failed: ${attempt.name}`, error)
        try {
          await trialScanner.stop()
        } catch {
          // 失败分支下可能尚未进入 running，忽略 stop 异常。
        }
        try {
          await trialScanner.clear()
        } catch {
          // 清理失败不阻塞下一轮回退。
        }
      }
    }

    throw startError ?? new Error('摄像头初始化失败')
  }

  async function initialize(forceRestart = false): Promise<void> {
    if (starting.value) {
      return
    }

    if (ready.value && !forceRestart) {
      return
    }

    starting.value = true
    if (forceRestart) {
      await stop()
    }

    try {
      await startScanner()
    } catch (error) {
      ready.value = false
      throw new Error(mapStartErrorMessage(error))
    } finally {
      starting.value = false
    }
  }

  function pause(): void {
    if (!scanner) {
      return
    }

    try {
      scanner.pause(true)
    } catch {
      // 某些浏览器不支持 pause，忽略后继续流程。
    }
  }

  function resume(): void {
    if (!scanner) {
      return
    }

    try {
      scanner.resume()
    } catch {
      // 不支持 resume 时，下一帧扫描依然可继续。
    }
  }

  async function stop(): Promise<void> {
    if (!scanner) {
      ready.value = false
      return
    }

    try {
      await scanner.stop()
    } catch {
      // 未处于扫描状态时 stop 可能抛错，忽略即可。
    }

    try {
      await scanner.clear()
    } catch {
      // 清理失败不阻塞页面卸载。
    }

    scanner = null
    ready.value = false
  }

  return {
    ready,
    starting,
    initialize,
    pause,
    resume,
    stop,
  }
}
