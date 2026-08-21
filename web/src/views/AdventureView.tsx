import { useEffect, useState } from 'react'
import { AdventurerStrip } from '../components/AdventurerStrip'
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

      {/*
        Nine flex-1 pills in a wrapping row is three lines of 43px-wide buttons at 390px,
        and "Chronicle" does not fit in any of them. One line that scrolls keeps each label
        whole and keeps the panel you are on next to the ones either side of it.
        -mx-4 px-4 so the row bleeds to the screen edge: a scroller that stops short of the
        edge reads as a cut-off list rather than a scrollable one.
      */}
      <div
        className={
          isMobile
            ? '-mx-4 flex snap-x items-center gap-2 overflow-x-auto px-4 pb-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden'
            : 'flex flex-wrap items-center gap-0.5 rounded-lg border border-line bg-surface-sunk p-0.5'
        }
      >
        {PANELS.map((entry) => {
          const active = panel === entry.key

          return (
            <button
              key={entry.key}
              type="button"
              aria-pressed={active}
              onClick={() => setPanel(entry.key)}
              data-testid={`adventure-${entry.key}`}
              className={
                isMobile
                  ? `min-h-11 shrink-0 snap-start rounded-full px-4 py-2.5 text-[12.5px] font-medium whitespace-nowrap transition ${
                      active
                        ? 'bg-ink text-canvas'
                        : 'border border-line bg-surface text-ink-muted'
                    }`
                  : `flex-1 rounded-md px-2.5 py-1.5 text-[11.5px] font-medium whitespace-nowrap transition ${
                      active
                        ? 'bg-surface text-ink shadow-[0_1px_2px_rgb(0_0_0/0.1)]'
                        : 'text-ink-faint hover:text-ink-muted'
                    }`
              }
            >
              {entry.label}
            </button>
          )
        })}
      </div>

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

      <ClassSelect
        open={classOpen}
        currentClassKey={sheet.data.classKey}
        onClose={() => setClassOpen(false)}
      />
    </div>
  )
}
