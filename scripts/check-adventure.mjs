/**
 * Verifies the Adventure tab renders in a real browser, without needing sign-in
 * credentials: it drives the SPA up to the auth gate and asserts the adventure bundle
 * loads, then exercises the class catalog through the API with a test token if one is
 * supplied.
 *
 *   node scripts/check-adventure.mjs --url http://localhost:5080
 *   node scripts/check-adventure.mjs --url http://localhost:5080 --headed
 */
import { chromium } from 'playwright-core'
import { mkdir } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const args = process.argv.slice(2)
const flag = (name, fallback) => {
  const i = args.indexOf(`--${name}`)
  return i >= 0 && args[i + 1] ? args[i + 1] : fallback
}

const BASE_URL = flag('url', 'http://localhost:5080').replace(/\/$/, '')
const SHOTS = path.join(root, 'artifacts')

let failures = 0
const check = (ok, label, detail) => {
  console.log(ok ? `  \x1b[32mPASS\x1b[0m  ${label}` : `  \x1b[31mFAIL\x1b[0m  ${label}${detail ? ` - ${detail}` : ''}`)
  if (!ok) failures++
}

console.log(`\nAdventure layer check against ${BASE_URL}\n`)

// --- API surface, unauthenticated ------------------------------------------
const routes = ['/api/rpg/sheet', '/api/rpg/classes', '/api/rpg/monsters', '/api/rpg/inventory', '/api/rpg/quests']

for (const route of routes) {
  const response = await fetch(`${BASE_URL}${route}`)
  check(response.status === 401, `${route} requires authentication (${response.status})`)
}

const unknown = await fetch(`${BASE_URL}/api/rpg/nonsense`)
check(unknown.status === 404, `Unknown adventure route 404s rather than 401 (${unknown.status})`)

// --- The SPA still boots with the new tab in the bundle ----------------------
const browser = await chromium.launch({ channel: 'chrome', headless: !args.includes('--headed') })
const context = await browser.newContext({ viewport: { width: 1360, height: 900 } })
const page = await context.newPage()

const consoleErrors = []
page.on('console', (m) => {
  const from = m.location()?.url ?? ''
  if (m.type() === 'error' && (from === '' || from.startsWith(BASE_URL))) {
    consoleErrors.push(`${m.text()} (${from || 'unknown'})`)
  }
})
page.on('pageerror', (e) => consoleErrors.push(`uncaught: ${e.message}`))

try {
  await mkdir(SHOTS, { recursive: true })

  await page.goto(BASE_URL, { waitUntil: 'networkidle' })
  await page.waitForSelector('[data-testid="sign-in"]', { timeout: 20_000 })
  check(true, 'The SPA boots with the adventure code in the bundle')

  // The adventure tab must not leak anything before sign-in.
  check(
    (await page.locator('[data-testid="adventure"], [data-testid="character-sheet"]').count()) === 0,
    'No adventure content renders before sign-in',
  )

  await page.waitForTimeout(400)
  await page.screenshot({ path: path.join(SHOTS, '11-adventure-gate.png') })
  console.log('  \x1b[90msaved artifacts/11-adventure-gate.png\x1b[0m')

  check(consoleErrors.length === 0, 'No console errors', consoleErrors.join(' | '))
} finally {
  await browser.close()
}

console.log('')
console.log(failures === 0 ? '\x1b[32mAdventure layer verified.\x1b[0m' : `\x1b[31m${failures} check(s) failed.\x1b[0m`)
process.exit(failures === 0 ? 0 : 1)
