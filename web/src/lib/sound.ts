/**
 * A small synthesiser for combat feel. Ten cues, no asset files, no library, no network.
 *
 * Two rules shape the whole module. Nothing here runs at import time, so a page that never
 * plays a cue never builds an audio graph. And the AudioContext is constructed on the first
 * cue, which can only happen after a click, because a context created without a user gesture
 * starts suspended and logs a warning on every load.
 */

export type Cue =
  | 'attack'
  | 'hit'
  | 'critical'
  | 'miss'
  | 'kill'
  | 'defeat'
  | 'flee'
  | 'coin'
  | 'drop'
  | 'levelUp'

export const SOUND_STORAGE_KEY = 'questward.sound'

/** Loud enough to hear over a room, quiet enough to sit under a conversation. */
const MASTER_GAIN = 0.25

/** A round appends several rolls at once, so the same cue repeated inside this window is one event. */
const DEBOUNCE_MS = 40

// ----------------------------------------------------------------- preference

let enabled: boolean | null = null
const listeners = new Set<() => void>()

/**
 * On unless the player has said otherwise.
 *
 * The comparison is against 'off' rather than 'on' so that an absent key means on: the cues are
 * the only feedback a dice roll has, and a fight played in silence reads as a fight that did not
 * roll anything. Only an explicit 'off' silences it, and it survives every reload.
 */
function readStored(): boolean {
  try {
    return localStorage.getItem(SOUND_STORAGE_KEY) !== 'off'
  } catch {
    // Private mode or storage disabled. The choice cannot be read or written, so the default
    // is all there is, and the default is on.
    return true
  }
}

/**
 * There is no prefers-reduced-sound query, so reduced motion is the nearest standing signal
 * that a user does not want incidental sensory effects. It is read to describe the setting and
 * never to change it: sound is on by default for everyone, and one button turns it off.
 */
export function prefersReducedMotion(): boolean {
  try {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches
  } catch {
    return false
  }
}

export function isSoundOn(): boolean {
  if (enabled === null) enabled = readStored()

  return enabled
}

export function setSoundOn(on: boolean): void {
  enabled = on

  try {
    localStorage.setItem(SOUND_STORAGE_KEY, on ? 'on' : 'off')
  } catch {
    // Not fatal - the choice simply will not survive a reload.
  }

  listeners.forEach((listener) => listener())
}

export function subscribeSound(listener: () => void): () => void {
  listeners.add(listener)

  return () => {
    listeners.delete(listener)
  }
}

// --------------------------------------------------------------------- graph

interface Graph {
  ctx: AudioContext
  out: GainNode
}

let graphed: Graph | null = null
let noiseBuffer: AudioBuffer | null = null

function graph(): Graph | null {
  if (graphed) return graphed

  const Ctor =
    window.AudioContext ??
    (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext

  if (!Ctor) return null

  const ctx = new Ctor()
  const out = ctx.createGain()
  out.gain.value = MASTER_GAIN
  out.connect(ctx.destination)

  graphed = { ctx, out }
  return graphed
}

/** One tenth of a second of white noise, filled once and reused by every noise cue. */
function noise(ctx: AudioContext): AudioBuffer {
  if (noiseBuffer) return noiseBuffer

  const buffer = ctx.createBuffer(1, Math.floor(ctx.sampleRate * 0.1), ctx.sampleRate)
  const samples = buffer.getChannelData(0)
  for (let i = 0; i < samples.length; i++) samples[i] = Math.random() * 2 - 1

  noiseBuffer = buffer
  return buffer
}

/** Exponential ramps cannot touch zero, hence the near-silent floor at both ends. */
function envelope(gain: GainNode, at: number, peak: number, seconds: number) {
  gain.gain.setValueAtTime(0.0001, at)
  gain.gain.exponentialRampToValueAtTime(peak, at + Math.min(0.008, seconds / 4))
  gain.gain.exponentialRampToValueAtTime(0.0001, at + seconds)
}

interface ToneOptions {
  type: OscillatorType
  from: number
  to?: number
  at: number
  seconds: number
  peak: number
  /** Set to run the oscillator through a lowpass, which is what makes a swipe a swipe. */
  lowpass?: number
}

function tone(g: Graph, options: ToneOptions) {
  const { type, from, to, at, seconds, peak, lowpass } = options

  const osc = g.ctx.createOscillator()
  const gain = g.ctx.createGain()
  osc.type = type
  osc.frequency.setValueAtTime(from, at)
  if (to !== undefined) osc.frequency.exponentialRampToValueAtTime(to, at + seconds)
  envelope(gain, at, peak, seconds)

  const filter = lowpass === undefined ? null : g.ctx.createBiquadFilter()
  if (filter && lowpass !== undefined) {
    filter.type = 'lowpass'
    filter.frequency.setValueAtTime(lowpass, at)
    osc.connect(filter)
    filter.connect(gain)
  } else {
    osc.connect(gain)
  }

  gain.connect(g.out)
  osc.start(at)
  osc.stop(at + seconds + 0.02)

  // Nodes are cheap but not free, and a long fight fires hundreds of them.
  osc.onended = () => {
    osc.disconnect()
    filter?.disconnect()
    gain.disconnect()
  }
}

interface BurstOptions {
  filter: BiquadFilterType
  frequency: number
  /** Ramps the filter, turning a flat hiss into a sweep. */
  to?: number
  at: number
  seconds: number
  peak: number
}

function burst(g: Graph, options: BurstOptions) {
  const { filter, frequency, to, at, seconds, peak } = options

  const source = g.ctx.createBufferSource()
  source.buffer = noise(g.ctx)

  const band = g.ctx.createBiquadFilter()
  band.type = filter
  band.frequency.setValueAtTime(frequency, at)
  if (to !== undefined) band.frequency.exponentialRampToValueAtTime(to, at + seconds)

  const gain = g.ctx.createGain()
  envelope(gain, at, peak, seconds)

  source.connect(band)
  band.connect(gain)
  gain.connect(g.out)
  source.start(at)
  source.stop(at + seconds)

  source.onended = () => {
    source.disconnect()
    band.disconnect()
    gain.disconnect()
  }
}

// ---------------------------------------------------------------------- cues

/**
 * A critical is the hit cue with a bell on top, and a drop is the coin cue with a chime on
 * top, so the louder event reads as the same thing plus something rather than as a different
 * thing. That is also what leaves room for rarity to add a third partial later.
 */
const CUES: Record<Cue, (g: Graph, at: number) => void> = {
  attack: (g, at) =>
    tone(g, { type: 'sawtooth', from: 180, to: 90, at, seconds: 0.08, peak: 0.35, lowpass: 1200 }),

  hit: (g, at) => burst(g, { filter: 'lowpass', frequency: 900, at, seconds: 0.06, peak: 0.9 }),

  critical: (g, at) => {
    CUES.hit(g, at)
    tone(g, { type: 'triangle', from: 660, at, seconds: 0.18, peak: 0.5 })
  },

  miss: (g, at) => burst(g, { filter: 'highpass', frequency: 2000, at, seconds: 0.04, peak: 0.45 }),

  kill: (g, at) => {
    tone(g, { type: 'triangle', from: 440, at, seconds: 0.15, peak: 0.5 })
    tone(g, { type: 'triangle', from: 294, at: at + 0.15, seconds: 0.15, peak: 0.5 })
  },

  defeat: (g, at) => tone(g, { type: 'sine', from: 110, at, seconds: 0.6, peak: 0.6 }),

  flee: (g, at) =>
    burst(g, { filter: 'bandpass', frequency: 300, to: 2400, at, seconds: 0.12, peak: 0.5 }),

  coin: (g, at) => {
    tone(g, { type: 'square', from: 880, at, seconds: 0.05, peak: 0.22 })
    tone(g, { type: 'square', from: 1320, at: at + 0.06, seconds: 0.05, peak: 0.22 })
  },

  drop: (g, at) => {
    CUES.coin(g, at)
    tone(g, { type: 'triangle', from: 880, at: at + 0.02, seconds: 0.3, peak: 0.35 })
  },

  // The only cue allowed to sound pleased, and it belongs to finishing a task.
  levelUp: (g, at) => {
    tone(g, { type: 'triangle', from: 440, at, seconds: 0.14, peak: 0.45 })
    tone(g, { type: 'triangle', from: 554, at: at + 0.12, seconds: 0.14, peak: 0.45 })
    tone(g, { type: 'triangle', from: 659, at: at + 0.24, seconds: 0.16, peak: 0.45 })
  },
}

const lastPlayed = new Map<Cue, number>()

/**
 * @param delay Seconds to schedule the cue ahead of now, so a round's kill and its gold do
 * not land on the same instant and mask each other. Scheduled on the audio clock rather
 * than with a timer, which keeps the spacing exact.
 */
export function play(cue: Cue, delay = 0): void {
  if (!isSoundOn()) return

  const now = Date.now()
  if (now - (lastPlayed.get(cue) ?? 0) < DEBOUNCE_MS) return
  lastPlayed.set(cue, now)

  const g = graph()
  if (!g) return

  // A context built during a gesture can still come up suspended on mobile Safari.
  if (g.ctx.state === 'suspended') void g.ctx.resume()

  try {
    CUES[cue](g, g.ctx.currentTime + delay)
  } catch {
    // Audio is decoration. A device that refuses to make a noise must never break a fight.
  }
}
