/**
 * Drives the real installed Chrome through the Questward UI and asserts the whole
 * gamification loop actually works in a browser: adding tasks, earning XP, levelling
 * up, unlocking badges, reopening, and switching themes.
 *
 * Uses playwright-core with `channel: 'chrome'`, so it launches the Chrome already on
 * the machine rather than downloading a browser build.
 *
 * Every /api route now requires a token, so the run starts by signing in through Auth0
 * Universal Login with a dedicated test user. Credentials come from the command line or
 * from QUESTWARD_TEST_USER / QUESTWARD_TEST_PASSWORD.
 *
 *   node scripts/verify-ui.mjs --username you@example.com --password 'secret'
 *   node scripts/verify-ui.mjs --url http://localhost:8080 --username ... --password ...
 *   node scripts/verify-ui.mjs --headed              # watch it happen
 *
 * Known risk: Auth0 bot detection can block automated form submission. If sign-in times
 * out, disable Bot Detection for the development tenant (Security -> Attack Protection).
 */
import { chromium } from 'playwright-core'
import { mkdir, rm } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const args = process.argv.slice(2)
const readFlag = (name, fallback) => {
  const index = args.indexOf(`--${name}`)
  return index >= 0 && args[index + 1] ? args[index + 1] : fallback
}

const BASE_URL = readFlag('url', 'http://localhost:5080').replace(/\/$/, '')
const HEADED = args.includes('--headed')
const SHOTS = path.join(root, 'artifacts')

const USERNAME = readFlag('username', process.env.QUESTWARD_TEST_USER)
const PASSWORD = readFlag('password', process.env.QUESTWARD_TEST_PASSWORD)

if (!USERNAME || !PASSWORD) {
  console.error(
    '\nSign-in credentials are required now that the API is authenticated.\n' +
      '  node scripts/verify-ui.mjs --username you@example.com --password <pw>\n' +
      'or set QUESTWARD_TEST_USER and QUESTWARD_TEST_PASSWORD.\n',
  )
  process.exit(2)
}

let failures = 0
const pass = (label) => console.log(`  \x1b[32mPASS\x1b[0m  ${label}`)
const fail = (label, detail) => {
  failures++
  console.log(`  \x1b[31mFAIL\x1b[0m  ${label}${detail ? ` - ${detail}` : ''}`)
}

const check = (condition, label, detail) =>
  condition ? pass(label) : fail(label, detail)

const checkEqual = (actual, expected, label) =>
  actual === expected ? pass(`${label} (= ${actual})`) : fail(label, `expected ${expected}, got ${actual}`)

const step = (label) => console.log(`\n\x1b[36m${label}\x1b[0m`)

/** Screenshots are taken after animations settle, or they catch layout mid-flight. */
async function shoot(page, name, settleMs = 700) {
  await page.waitForTimeout(settleMs)
  await page.screenshot({ path: path.join(SHOTS, `${name}.png`), fullPage: false })
  console.log(`  \x1b[90msaved artifacts/${name}.png\x1b[0m`)
}

/**
 * Drives Auth0 Universal Login. Deliberately the real hosted form rather than a token
 * injected into storage, so what gets verified is the path a user actually takes.
 */
async function signIn(page) {
  await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' })

  // The app fetches /api/config before it can offer sign-in at all.
  await page.waitForSelector('[data-testid="sign-in"]', { timeout: 20_000 })
  await page.click('[data-testid="sign-in"]')

  await page.waitForURL(/auth0\.com|\/u\/login/, { timeout: 20_000 })

  await page.fill('input[name="username"], input[name="email"], input[type="email"]', USERNAME)
  await page.fill('input[name="password"], input[type="password"]', PASSWORD)
  await page.click('button[type="submit"], button[name="action"]')

  // Auth0 shows a consent screen the first time for some tenant configurations.
  const consent = page.locator('button[value="accept"], button[name="action"][value="accept"]')
  await consent.click({ timeout: 5000 }).catch(() => {})

  await page.waitForURL((url) => url.toString().startsWith(BASE_URL), { timeout: 30_000 })
  await page.waitForSelector('[data-testid="character-card"]', { timeout: 30_000 })
}

async function main() {
  await rm(SHOTS, { recursive: true, force: true })
  await mkdir(SHOTS, { recursive: true })

  console.log(`\nQuestward UI verification against ${BASE_URL}`)

  const browser = await chromium.launch({ channel: 'chrome', headless: !HEADED })
  const context = await browser.newContext({ viewport: { width: 1360, height: 900 } })
  const page = await context.newPage()

  // Anything logged as an error or a failed request is a verification failure,
  // not something to notice afterwards in the console.
  const consoleErrors = []
  const failedRequests = []

  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text())
  })
  page.on('pageerror', (error) => consoleErrors.push(`uncaught: ${error.message}`))
  page.on('requestfailed', (request) =>
    failedRequests.push(`${request.method()} ${request.url()} - ${request.failure()?.errorText}`),
  )
  page.on('response', (response) => {
    // Only our own origin. The Auth0 login flow legitimately produces 4xx responses of
    // its own (probe requests, consent checks) that say nothing about this app.
    if (response.url().startsWith(BASE_URL) && response.status() >= 400) {
      failedRequests.push(`${response.request().method()} ${response.url()} -> ${response.status()}`)
    }
  })

  // Lifted straight off the app's own requests, so the script's direct API calls carry
  // the same credentials the browser is using. Reading the SDK's storage format instead
  // would couple this script to an internal detail of auth0-spa-js.
  let bearer = null
  page.on('request', (request) => {
    const header = request.headers()['authorization']
    if (header?.startsWith('Bearer ')) bearer = header
  })

  const api = context.request
  const authed = () => ({ headers: { authorization: bearer } })
  const readCharacter = async () =>
    (await api.get(`${BASE_URL}/api/character`, authed())).json()

  try {
    // ------------------------------------------------------------- sign in
    step('[auth] signing in through Auth0')
    await signIn(page)
    pass(`Signed in as ${USERNAME}`)

    check(Boolean(bearer), 'The app attaches a bearer token to its API calls')

    // ---------------------------------------------------------------- setup
    step('[setup] clearing the task list so the run starts from a known board')
    const existing = await (await api.get(`${BASE_URL}/api/tasks`, authed())).json()
    for (const task of existing) {
      await api.delete(`${BASE_URL}/api/tasks/${task.id}`, authed())
    }

    await page.reload({ waitUntil: 'networkidle' })

    const before = await readCharacter()
    console.log(
      `  starting at level ${before.level} (${before.title}), ${before.totalXp} XP, ` +
        `${before.achievementsUnlocked}/${before.achievementsTotal} badges`,
    )

    // ------------------------------------------------------------- first load
    step('[load] the app renders and matches the character API')
    await page.waitForSelector('[data-testid="character-card"]')
    checkEqual(
      await page.getAttribute('[data-testid="level-badge"]', 'data-level'),
      String(before.level),
      'Header level badge matches the API',
    )
    checkEqual(
      await page.textContent('[data-testid="xp-into-level"]'),
      String(before.xpIntoLevel),
      'XP readout matches the API',
    )
    checkEqual(
      await page.textContent('[data-testid="character-title"]'),
      before.title,
      'Rank title matches the API',
    )
    check(
      (await page.locator('[data-testid="task-list-empty"]').count()) === 1,
      'Empty board shows the empty state',
    )

    await shoot(page, '01-empty-light')

    // ------------------------------------------------------------- add tasks
    step('[add] creating one task per difficulty through the UI')
    const additions = [
      { title: 'Repot the monstera', difficulty: 'easy' },
      { title: 'Write the weekly review', difficulty: 'medium' },
      { title: 'Refactor the billing module', difficulty: 'hard' },
      { title: 'Ship the self-hosted release', difficulty: 'epic' },
    ]

    for (const addition of additions) {
      await page.click(`[data-testid="difficulty-option-${addition.difficulty}"]`)
      await page.fill('[data-testid="quick-add-input"]', addition.title)
      await page.click('[data-testid="quick-add-submit"]')
      await page.waitForSelector(`[data-task-title="${addition.title}"]`)
    }

    checkEqual(await page.locator('[data-testid="task-card"]').count(), 4, 'Four tasks are listed')
    check(
      (await page.locator('[data-testid="task-card"]').first().getAttribute('data-task-title')) ===
        additions[3].title,
      'The newest task sorts to the top',
    )

    await shoot(page, '02-tasks-light')

    // ----------------------------------------------------------- complete one
    step('[complete] finishing the Medium task grants 25 XP')
    const medium = page.locator('[data-task-title="Write the weekly review"]')
    await medium.locator('[data-testid="task-toggle"]').click()

    await page.waitForSelector('[data-testid="xp-float"]', { timeout: 3000 })
    const floatText = await page.textContent('[data-testid="xp-float"]')
    checkEqual(floatText?.trim(), '+25 XP', 'A "+25 XP" number floats up from the task')

    await page.waitForFunction(
      (expected) =>
        document.querySelector('[data-testid="total-xp"]')?.textContent?.replace(/,/g, '') ===
        String(expected),
      before.totalXp + 25,
      { timeout: 5000 },
    )
    pass(`Header total XP rose to ${before.totalXp + 25}`)

    const afterMedium = await readCharacter()
    checkEqual(afterMedium.totalXp, before.totalXp + 25, 'API agrees the character gained 25 XP')
    check(
      (await page.locator('[data-testid="completed-section"]').count()) === 1,
      'A Completed section appears',
    )
    checkEqual(
      await page.locator('[data-testid="task-card"][data-task-title="Write the weekly review"]').count(),
      1,
      'The completed task appears exactly once (no duplicate during the move)',
    )
    checkEqual(
      await page
        .locator('[data-testid="completed-section"] [data-task-title="Write the weekly review"]')
        .getAttribute('data-completed'),
      'true',
      'It now sits in the Completed section',
    )

    // -------------------------------------------------------------- level up
    step('[level] completing Epic tasks until the level actually ticks over')
    const epicsNeeded = Math.max(1, Math.ceil(afterMedium.xpToNextLevel / 100))
    console.log(`  ${afterMedium.xpToNextLevel} XP to level ${afterMedium.level + 1}: needs ${epicsNeeded} Epic task(s)`)

    for (let index = 1; index < epicsNeeded; index++) {
      await page.click('[data-testid="difficulty-option-epic"]')
      await page.fill('[data-testid="quick-add-input"]', `Filler epic ${index}`)
      await page.click('[data-testid="quick-add-submit"]')
      await page.waitForSelector(`[data-task-title="Filler epic ${index}"]`)
      await page
        .locator(`[data-task-title="Filler epic ${index}"] [data-testid="task-toggle"]`)
        .click()
      await page.waitForTimeout(400)

      // Only the final completion should be the one that crosses the threshold.
      if ((await page.locator('[data-testid="level-up-overlay"]').count()) > 0) {
        await page.click('[data-testid="level-up-dismiss"]')
        await page.waitForSelector('[data-testid="level-up-overlay"]', { state: 'detached' })
      }
    }

    await page
      .locator('[data-task-title="Ship the self-hosted release"] [data-testid="task-toggle"]')
      .click()

    await page.waitForSelector('[data-testid="level-up-overlay"]', { timeout: 5000 })
    pass('The level-up overlay appears')

    const shownLevel = Number(await page.getAttribute('[data-testid="level-up-overlay"]', 'data-level'))
    check(shownLevel > before.level, `Overlay shows a higher level (${before.level} -> ${shownLevel})`)
    checkEqual(
      await page.textContent('[data-testid="level-up-number"]'),
      String(shownLevel),
      'The medallion shows the new level',
    )

    await page.waitForTimeout(700)
    await shoot(page, '03-level-up')

    await page.click('[data-testid="level-up-dismiss"]')
    await page.waitForSelector('[data-testid="level-up-overlay"]', { state: 'detached' })
    pass('The overlay dismisses on Onward')

    const afterEpic = await readCharacter()
    checkEqual(afterEpic.level, shownLevel, 'API level matches what the overlay claimed')
    checkEqual(
      await page.getAttribute('[data-testid="level-badge"]', 'data-level'),
      String(afterEpic.level),
      'Header badge caught up to the new level',
    )
    checkEqual(
      await page.textContent('[data-testid="character-title"]'),
      afterEpic.title,
      'Character card shows the new rank title',
    )

    // --------------------------------------------------------------- badges
    step('[badges] the achievements earned along the way are shown as unlocked')
    // Let the badge toasts expire so they are not sitting on top of the screenshots.
    await page
      .locator('[data-testid="achievement-toast"]')
      .first()
      .waitFor({ state: 'detached', timeout: 8000 })
      .catch(() => {})

    await page.click('[data-testid="tab-badges"]')
    await page.waitForSelector('[data-testid="achievement-grid"]')

    for (const key of ['first-blood', 'epic-slayer', 'deep-work']) {
      checkEqual(
        await page.getAttribute(`[data-key="${key}"]`, 'data-unlocked'),
        'true',
        `Badge "${key}" is unlocked`,
      )
    }

    const lockedCount = await page.locator('[data-unlocked="false"]').count()
    check(lockedCount > 0, `Unearned badges stay locked (${lockedCount} locked)`)

    await shoot(page, '04-badges-light')

    // ---------------------------------------------------------------- record
    step('[record] the stats view renders its charts')
    await page.click('[data-testid="tab-record"]')
    await page.waitForSelector('[data-testid="stats-panel"]')

    check(
      (await page.locator('[data-testid="activity-chart"]').count()) === 1,
      'The 14-day activity chart renders',
    )
    check(
      (await page.locator('[data-testid="difficulty-chart"]').count()) === 1,
      'The difficulty breakdown renders',
    )
    check(
      (await page.locator('[data-testid="rank-ladder"] [data-current="true"]').count()) === 1,
      'The rank ladder highlights exactly one current rank',
    )

    await shoot(page, '05-record-light')

    // ---------------------------------------------------------------- reopen
    step('[reopen] un-completing a task refunds its XP but keeps the badge')
    await page.click('[data-testid="tab-tasks"]')
    await page.waitForSelector('[data-testid="task-list"]')

    const xpBeforeReopen = (await readCharacter()).totalXp
    await page
      .locator('[data-task-title="Ship the self-hosted release"] [data-testid="task-toggle"]')
      .click()

    await page.waitForFunction(
      (expected) =>
        document.querySelector('[data-testid="total-xp"]')?.textContent?.replace(/,/g, '') ===
        String(expected),
      xpBeforeReopen - 100,
      { timeout: 5000 },
    )
    pass(`Header total XP fell back to ${xpBeforeReopen - 100}`)

    checkEqual(
      (await readCharacter()).totalXp,
      xpBeforeReopen - 100,
      'API agrees the 100 XP was refunded',
    )

    await page.click('[data-testid="tab-badges"]')
    await page.waitForSelector('[data-testid="achievement-grid"]')
    checkEqual(
      await page.getAttribute('[data-key="epic-slayer"]', 'data-unlocked'),
      'true',
      'Epic Slayer survives the reopen',
    )
    await page.click('[data-testid="tab-tasks"]')

    // ------------------------------------------------------------- character
    step('[character] renaming the character persists')
    await page.click('[data-testid="character-edit"]')
    await page.fill('[data-testid="character-name-input"]', 'Wayfarer')
    await page.click('[data-testid="character-save"]')
    await page.waitForFunction(
      () => document.querySelector('[data-testid="character-name"]')?.textContent === 'Wayfarer',
      undefined,
      { timeout: 5000 },
    )
    checkEqual((await readCharacter()).name, 'Wayfarer', 'The new name reached the API')

    // ----------------------------------------------------------------- theme
    step('[theme] light / dark / system all take effect')
    const htmlClasses = () => page.evaluate(() => document.documentElement.className)

    await page.click('[data-theme-option="dark"]')
    await page.waitForFunction(() => document.documentElement.classList.contains('dark'))
    check((await htmlClasses()).includes('dark'), 'Dark applies the dark class')
    checkEqual(
      await page.evaluate(() => localStorage.getItem('questward.theme')),
      'dark',
      'Dark is persisted to localStorage',
    )
    await page.waitForTimeout(350)
    await shoot(page, '06-tasks-dark')

    await page.click('[data-testid="tab-badges"]')
    await page.waitForSelector('[data-testid="achievement-grid"]')
    await shoot(page, '07-badges-dark')
    await page.click('[data-testid="tab-record"]')
    await page.waitForSelector('[data-testid="stats-panel"]')
    await shoot(page, '08-record-dark')
    await page.click('[data-testid="tab-tasks"]')

    await page.click('[data-theme-option="light"]')
    await page.waitForFunction(() => !document.documentElement.classList.contains('dark'))
    check(!(await htmlClasses()).includes('dark'), 'Light removes the dark class')

    await page.click('[data-theme-option="system"]')
    checkEqual(
      await page.evaluate(() => localStorage.getItem('questward.theme')),
      'system',
      'System is persisted to localStorage',
    )
    const systemPrefersDark = await page.evaluate(
      () => window.matchMedia('(prefers-color-scheme: dark)').matches,
    )
    checkEqual(
      (await htmlClasses()).includes('dark'),
      systemPrefersDark,
      'System follows the OS preference',
    )

    step('[theme] the choice survives a reload without flashing')
    await page.click('[data-theme-option="dark"]')
    await page.reload({ waitUntil: 'domcontentloaded' })
    check(
      await page.evaluate(() => document.documentElement.classList.contains('dark')),
      'Dark is applied before the app hydrates',
    )
    await page.click('[data-theme-option="light"]')

    // ---------------------------------------------------------------- mobile
    const MOBILE_TASK = 'Added from the sheet'

    step('[responsive] the layout holds at phone width')
    await page.setViewportSize({ width: 390, height: 844 })
    await page.waitForTimeout(400)

    const overflows = await page.evaluate(
      () => document.documentElement.scrollWidth > window.innerWidth + 1,
    )
    check(!overflows, 'No horizontal overflow at 390px')
    checkEqual(
      await page.locator('[data-testid="xp-rail"]').count(),
      1,
      'Exactly one XP rail exists in the DOM',
    )
    check(await page.locator('[data-testid="xp-rail"]').isVisible(), 'The XP rail is visible on mobile')

    // The compact rail drops the 40px badge, and the sections moved to the bottom bar.
    checkEqual(
      await page.locator('[data-testid="level-badge"]').count(),
      0,
      'The compact rail drops the level badge',
    )
    checkEqual(
      await page.locator('[data-testid="bottom-nav"]').count(),
      1,
      'The bottom bar exists at 390px',
    )
    checkEqual(
      await page.locator('[data-testid="tab-tasks"]').count(),
      0,
      'The top tab strip is gone below sm',
    )

    for (const key of ['tasks', 'adventure', 'record', 'badges', 'add']) {
      const box = await page.locator(`[data-testid="bottom-nav-${key}"]`).boundingBox()
      check(
        Boolean(box) && box.height >= 44,
        `The ${key} target clears 44px (${box ? Math.round(box.height) : 0}px)`,
      )
    }

    await shoot(page, '09-mobile-light')

    // The add form is a sheet on a phone, so this is the mobile equivalent of the desktop
    // quick-add path above. It also proves the sheet primitive: the page behind is pinned
    // rather than merely hidden, and focus actually lands inside.
    step('[responsive] the add sheet takes a quest')
    await page.click('[data-testid="bottom-nav-add"]')
    await page.waitForSelector('[data-testid="add-sheet"]')

    check(
      await page.evaluate(() => getComputedStyle(document.body).position === 'fixed'),
      'The page behind the sheet is scroll-locked',
    )
    check(
      await page.evaluate(() =>
        document.querySelector('[data-testid="add-sheet"]')?.contains(document.activeElement),
      ),
      'Focus moves into the sheet',
    )

    await page.click('[data-testid="add-sheet"] [data-testid="difficulty-option-easy"]')
    await page.fill('[data-testid="add-sheet"] [data-testid="quick-add-input"]', MOBILE_TASK)
    await page.click('[data-testid="add-sheet"] [data-testid="quick-add-submit"]')
    await page.waitForSelector('[data-testid="add-sheet"]', { state: 'detached' })
    await page.waitForSelector(`[data-task-title="${MOBILE_TASK}"]`)
    check(true, 'A quest added from the sheet reaches the board')

    check(
      await page.evaluate(() => getComputedStyle(document.body).position !== 'fixed'),
      'The scroll lock lifts when the sheet closes',
    )

    // Theme moved behind the avatar, so it is reached through the account sheet now.
    // Scoped to the sheet on purpose: this fails loudly if the toggle is ever left mounted
    // in the header as well, rather than passing against the wrong node.
    const themeOnMobile = async (value) => {
      await page.click('[data-testid="account-menu"]')
      await page.waitForSelector('[data-testid="account-sheet"]')
      await page.click(`[data-testid="account-sheet"] [data-theme-option="${value}"]`)
      await page.click('[data-testid="account-sheet-close"]')
      await page.waitForSelector('[data-testid="account-sheet"]', { state: 'detached' })
    }

    await themeOnMobile('dark')
    await page.waitForFunction(() => document.documentElement.classList.contains('dark'))
    check(true, 'Dark can be chosen from the account sheet on mobile')
    await shoot(page, '10-mobile-dark')
    await themeOnMobile('light')

    // ------------------------------------------------------------ diagnostics
    step('[diagnostics] console and network stayed clean')
    checkEqual(consoleErrors.length, 0, 'No console errors')
    if (consoleErrors.length) consoleErrors.forEach((error) => console.log(`        ${error}`))

    checkEqual(failedRequests.length, 0, 'No failed requests')
    if (failedRequests.length) failedRequests.forEach((request) => console.log(`        ${request}`))
  } finally {
    await browser.close()
  }

  console.log('')
  if (failures === 0) {
    console.log('\x1b[32mAll UI checks passed.\x1b[0m')
    process.exit(0)
  }

  console.log(`\x1b[31m${failures} UI check(s) failed.\x1b[0m`)
  process.exit(1)
}

main().catch((error) => {
  console.error('\nVerification crashed:', error)
  process.exit(1)
})
