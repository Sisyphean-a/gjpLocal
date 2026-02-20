let audioContext: AudioContext | null = null

function ensureAudioContext(): AudioContext {
  if (!audioContext) {
    audioContext = new AudioContext()
  }

  if (audioContext.state === 'suspended') {
    void audioContext.resume()
  }

  return audioContext
}

function playTone(frequency: number, durationMs: number, type: OscillatorType): Promise<void> {
  const ctx = ensureAudioContext()
  const oscillator = ctx.createOscillator()
  const gain = ctx.createGain()

  oscillator.type = type
  oscillator.frequency.value = frequency
  gain.gain.value = 0.0001

  oscillator.connect(gain)
  gain.connect(ctx.destination)

  const now = ctx.currentTime
  gain.gain.exponentialRampToValueAtTime(0.18, now + 0.02)
  gain.gain.exponentialRampToValueAtTime(0.0001, now + durationMs / 1000)

  oscillator.start(now)
  oscillator.stop(now + durationMs / 1000)

  return new Promise((resolve) => {
    oscillator.onended = () => resolve()
  })
}

export async function playSuccessTone(): Promise<void> {
  await playTone(980, 120, 'square')
}

export async function playErrorTone(): Promise<void> {
  await playTone(220, 180, 'sawtooth')
}
