import { useEffect, useState } from 'react'
import { CharacterSheetPanel } from '../components/rpg/CharacterSheetPanel'
import { ClassSelect } from '../components/rpg/ClassSelect'
import { QuestBoard } from '../components/rpg/QuestBoard'
import { Tavern } from '../components/rpg/Tavern'
import { useInventory, useSheet } from '../lib/rpgQueries'

type Panel = 'sheet' | 'tavern' | 'quests'

const PANELS: { key: Panel; label: string }[] = [
  { key: 'sheet', label: 'Character' },
  { key: 'tavern', label: 'Tavern' },
  { key: 'quests', label: 'Quests' },
]

export function AdventureView() {
  const sheet = useSheet()
  const inventory = useInventory()
  const [panel, setPanel] = useState<Panel>('sheet')
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
      <div className="flex items-center gap-0.5 rounded-lg border border-line bg-surface-sunk p-0.5">
        {PANELS.map((entry) => {
          const active = panel === entry.key

          return (
            <button
              key={entry.key}
              type="button"
              aria-pressed={active}
              onClick={() => setPanel(entry.key)}
              data-testid={`adventure-${entry.key}`}
              className={`flex-1 rounded-md px-3 py-1.5 text-[12px] font-medium transition ${
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

      {panel === 'sheet' && (
        <CharacterSheetPanel
          sheet={sheet.data}
          inventory={inventory.data ?? []}
          onChangeClass={() => setClassOpen(true)}
        />
      )}

      {panel === 'tavern' && <Tavern sheet={sheet.data} />}
      {panel === 'quests' && <QuestBoard />}

      <ClassSelect
        open={classOpen}
        currentClassKey={sheet.data.classKey}
        onClose={() => setClassOpen(false)}
      />
    </div>
  )
}
