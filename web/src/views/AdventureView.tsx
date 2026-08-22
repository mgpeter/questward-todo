import { motion } from 'motion/react'
import { useEffect, useRef, useState } from 'react'
import { AdventurerStrip } from '../components/AdventurerStrip'
import { Ascend } from '../components/rpg/Ascend'
import { Bestiary } from '../components/rpg/Bestiary'
import { CharacterSheetPanel } from '../components/rpg/CharacterSheetPanel'
import { Chronicle } from '../components/rpg/Chronicle'
import { ClassSelect } from '../components/rpg/ClassSelect'
import { Dungeons } from '../components/rpg/Dungeons'
import { Hunts } from '../components/rpg/Hunts'
import { LoreCollection } from '../components/rpg/LoreCollection'
import { QuestBoard } from '../components/rpg/QuestBoard'
import { Shop } from '../components/rpg/Shop'
import { Tavern } from '../components/rpg/Tavern'
import { useNavigation, type AdventurePanel } from '../game/Navigation'
import { useInventory, useSheet } from '../lib/rpgQueries'
import { prefersReducedMotion } from '../lib/sound'
import { useIsMobile } from '../lib/useMediaQuery'

const PANELS: { key: AdventurePanel; label: string }[] = [
  { key: 'sheet', label: 'Character' },
  { key: 'tavern', label: 'Tavern' },

  // Next to the tavern, because that is the choice being made: one stamina, spent on a
  // stranger's monster or on one of your own.
  { key: 'hunts', label: 'Contracts' },
  { key: 'dungeons', label: 'Dungeons' },
  { key: 'shop', label: 'Market' },
  { key: 'quests', label: 'Quests' },
  { key: 'bestiary', label: 'Bestiary' },
  { key: 'lore', label: 'Lore' },
  { key: 'chronicle', label: 'Chronicle' },

  // Last, because it ends everything to its left. Shown at every level rather than hidden
  // below ten: a mechanic nobody can see is one nobody plans for.
  { key: 'ascend', label: 'Ascend' },
]

export function AdventureView() {
  const isMobile = useIsMobile()
  const sheet = useSheet()
  const inventory = useInventory()
  const { panel, setPanel } = useNavigation()
  const [classOpen, setClassOpen] = useState(false)

  // Prompt on first visit rather than assigning a class silently. Dismissible, and
  // reopenable from the character sheet.
  useEffect(() => {
    if (sheet.data && sheet.data.classKey === null) {
      setClassOpen(true)
    }
  }, [sheet.data])

  if (sheet.isLoading) {
    return <div className="panel h-96 animate-pulse rounded-2xl opacity-60" />
  }

  if (sheet.isError || !sheet.data) {
    return (
      <p role="alert" className="panel rounded-xl p-4 text-[13px] text-rose">
        Could not load your character: {(sheet.error as Error)?.message ?? 'unknown error'}
      </p>
    )
  }

  return (
    <div className="space-y-5" data-testid="adventure">
      {/*
        The same strip the task board carries. Health, stamina and gold are what every panel
        below spends, and they were previously readable only on the Character tab, so buying
        in the Market or picking a fight in the Tavern meant navigating away to find out
        whether you could afford it.
      */}
      <AdventurerStrip />

      {isMobile ? (
        <PanelTabs panel={panel} onSelect={setPanel} />
      ) : (
        <div className="flex flex-wrap items-center gap-0.5 rounded-lg border border-line bg-surface-sunk p-0.5">
          {PANELS.map((entry) => {
            const active = panel === entry.key

            return (
              <button
                key={entry.key}
                type="button"
                aria-pressed={active}
                onClick={() => setPanel(entry.key)}
                data-testid={`adventure-${entry.key}`}
                className={`flex-1 rounded-md px-2.5 py-1.5 text-[11.5px] font-medium whitespace-nowrap transition ${
                  active
                    ? 'bg-surface text-ink shadow-[0_1px_2px_rgb(0_0_0/0.1)]'
                    : 'text-ink-faint hover:text-ink-muted'
                }`}
              >
                {entry.label}
              </button>
            )
          })}
        </div>
      )}

      {panel === 'sheet' && (
        <CharacterSheetPanel
          sheet={sheet.data}
          inventory={inventory.data ?? []}
          onChangeClass={() => setClassOpen(true)}
        />
      )}

      {panel === 'tavern' && <Tavern sheet={sheet.data} inventory={inventory.data ?? []} />}
      {panel === 'hunts' && <Hunts sheet={sheet.data} inventory={inventory.data ?? []} />}
      {panel === 'dungeons' && (
        <Dungeons sheet={sheet.data} inventory={inventory.data ?? []} />
      )}
      {panel === 'shop' && <Shop />}
      {panel === 'quests' && <QuestBoard />}
      {panel === 'bestiary' && <Bestiary />}
      {panel === 'lore' && <LoreCollection />}
      {panel === 'chronicle' && <Chronicle />}
      {panel === 'ascend' && <Ascend sheet={sheet.data} />}

      <ClassSelect
        open={classOpen}
        currentClassKey={sheet.data.classKey}
        onClose={() => setClassOpen(false)}
      />
    </div>
  )
}

/**
 * The ten panels as a scrolling tab rail.
 *
 * Pills first, and they read as filter chips rather than as navigation: nothing said the row
 * moved, so the panels past the fold may as well not have existed. An underlined rail is
 * the shape a phone already uses for sections, and the two cues that it scrolls are structural
 * rather than instructional - the hairline runs past the last label it can fit, and the fade
 * turns a clipped word into an obvious edge instead of a broken one.
 */
function PanelTabs({
  panel,
  onSelect,
}: {
  panel: AdventurePanel
  onSelect: (panel: AdventurePanel) => void
}) {
  const railRef = useRef<HTMLDivElement>(null)

  // Arriving on a panel that is off-screen is the one case the fade cannot answer, and it is
  // the common one: TaskHuntSeal sends you straight to Contracts, which is third.
  useEffect(() => {
    const active = railRef.current?.querySelector<HTMLElement>('[aria-selected="true"]')

    active?.scrollIntoView({
      inline: 'center',
      block: 'nearest',
      // Someone who asked for less motion gets the jump, the same rule the combat log follows.
      behavior: prefersReducedMotion() ? 'auto' : 'smooth',
    })
  }, [panel])

  return (
    <div
      ref={railRef}
      role="tablist"
      aria-label="Adventure panels"
      // -mx-4 so the rail reaches the screen edge and a scrolled label is cut by the phone
      // rather than by a margin; px-5 so nothing rests against it at either end.
      className="-mx-4 flex snap-x items-stretch gap-1 overflow-x-auto border-b border-line px-5 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
      style={{
        maskImage:
          'linear-gradient(to right, transparent 0, #000 20px, #000 calc(100% - 28px), transparent 100%)',
      }}
    >
      {PANELS.map((entry) => {
        const active = panel === entry.key

        return (
          <button
            key={entry.key}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onSelect(entry.key)}
            data-testid={`adventure-${entry.key}`}
            className={`relative min-h-11 shrink-0 snap-start px-3 pb-2.5 text-[13px] font-medium whitespace-nowrap transition-colors ${
              active ? 'text-ink' : 'text-ink-faint'
            }`}
          >
            {entry.label}
            {active && (
              <motion.span
                // Its own id, so it never animates into the header's tab underline.
                layoutId="adventure-tab-underline"
                transition={{ type: 'spring', stiffness: 420, damping: 34 }}
                className="absolute inset-x-2 -bottom-px h-[2px] rounded-full bg-gold"
              />
            )}
          </button>
        )
      })}
    </div>
  )
}
