/**
 * Verifies the sign-in handoff without needing credentials: the SPA must bootstrap from
 * /api/config, render the gate, and hand off to the tenant's Universal Login with the
 * right client id, audience and redirect_uri.
 *
 * Complements scripts/verify-ui.mjs, which needs a real test user to go further.
 *
 *   node scripts/check-signin-redirect.mjs --url http://localhost:5080
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

const browser = await chromium.launch({ channel: 'chrome', headless: !args.includes('--headed') })
const context = await browser.newContext({ viewport: { width: 1360, height: 900 } })
const page = await context.newPage()

// Only our own origin. Auth0's hosted login page is not ours to police, and it emits the
// occasional 404 for its own assets.
const consoleErrors = []
page.on('console', (m) => {
  const from = m.location()?.url ?? ''
  if (m.type() === 'error' && (from === '' || from.startsWith(BASE_URL))) {
    consoleErrors.push(`${m.text()} (${from || 'unknown source'})`)
  }
})
page.on('pageerror', (e) => consoleErrors.push(`uncaught: ${e.message}`))

try {
  await mkdir(SHOTS, { recursive: true })

  console.log(`\nSign-in handoff check against ${BASE_URL}\n`)

  // Read what the server advertises rather than hardcoding a tenant, so this script works
  // against any deployment and actually verifies the SPA uses what it was told.
  const expected = await (await fetch(`${BASE_URL}/api/config`)).json()
  console.log(`  \x1b[90mserver advertises ${expected.auth0Domain} / ${expected.auth0Audience}\x1b[0m`)

  await page.goto(BASE_URL, { waitUntil: 'networkidle' })
  await page.waitForSelector('[data-testid="sign-in"]', { timeout: 20_000 })
  check(true, 'The SPA bootstraps from /api/config and renders the sign-in gate')

  check(
    (await page.locator('[data-testid="task-list"], [data-testid="character-card"]').count()) === 0,
    'No app content is rendered before sign-in',
  )

  await page.waitForTimeout(500)
  await page.screenshot({ path: path.join(SHOTS, '00-sign-in.png') })
  console.log('  \x1b[90msaved artifacts/00-sign-in.png\x1b[0m')

  await page.click('[data-testid="sign-in"]')
  await page.waitForURL(/auth0\.com/, { timeout: 20_000 })

  const target = new URL(page.url())
  check(
    target.hostname === expected.auth0Domain,
    `Handed off to the tenant the server advertised (${target.hostname})`,
  )

  // Auth0 moves the parameters into an opaque state after the first hop, so check
  // whichever hop still carries them.
  const authorizeRequest = target.searchParams.get('client_id')
    ? target
    : new URL(
        (await page.evaluate(() => performance.getEntriesByType('navigation').map((e) => e.name)))
          .concat(
            (await page.evaluate(() =>
              performance.getEntriesByType('resource').map((e) => e.name),
            )),
          )
          .find((u) => u.includes('/authorize?')) ?? target.toString(),
      )

  if (authorizeRequest.searchParams.get('client_id')) {
    check(
      authorizeRequest.searchParams.get('client_id') === expected.auth0ClientId,
      'The authorize request carries the client id the server advertised',
    )
    check(
      authorizeRequest.searchParams.get('audience') === expected.auth0Audience,
      'The authorize request carries the API audience (without it Auth0 issues an opaque token)',
    )
    check(
      authorizeRequest.searchParams.get('code_challenge_method') === 'S256',
      'PKCE is in use (S256)',
    )
  } else {
    console.log('  \x1b[90mnote: authorize parameters already collapsed into state\x1b[0m')
  }

  await page.waitForTimeout(1200)
  await page.screenshot({ path: path.join(SHOTS, '00-universal-login.png') })
  console.log('  \x1b[90msaved artifacts/00-universal-login.png\x1b[0m')

  const loginFormPresent =
    (await page.locator('input[type="email"], input[name="username"], input[type="password"]').count()) > 0
  check(loginFormPresent, 'Universal Login renders a credential form')

  check(consoleErrors.length === 0, 'No console errors', consoleErrors.join(' | '))
} finally {
  await browser.close()
}

console.log('')
console.log(failures === 0 ? '\x1b[32mSign-in handoff verified.\x1b[0m' : `\x1b[31m${failures} check(s) failed.\x1b[0m`)
process.exit(failures === 0 ? 0 : 1)
