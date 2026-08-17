import { useState } from 'react'
import { CharacterCard } from './components/CharacterCard'
import { Header, type TabKey } from './components/Header'
import { LevelUpOverlay } from './components/LevelUpOverlay'
import { ToastStack } from './components/ToastStack'
import { XpFloatLayer } from './components/XpFloatLayer'
import { useCharacter } from './lib/queries'
import { AdventureView } from './views/AdventureView'
import { BadgesView } from './views/BadgesView'
import { RecordView } from './views/RecordView'
import { TasksView } from './views/TasksView'

export default function App() {
  const [tab, setTab] = useState<TabKey>('tasks')
  const character = useCharacter()

  return (
    <div className="relative z-10 min-h-dvh">
      <Header
        character={character.data}
        tab={tab}
        onTabChange={setTab}
        badgeCount={character.data?.achievementsUnlocked ?? 0}
      />

      <main className="mx-auto grid max-w-6xl gap-5 px-4 py-6 lg:grid-cols-[290px_minmax(0,1fr)] lg:gap-6">
        <aside className="lg:sticky lg:top-32 lg:self-start">
          {character.data ? (
            <CharacterCard character={character.data} />
          ) : (
            <div className="panel h-80 animate-pulse rounded-2xl opacity-60" />
          )}
        </aside>

        <div className="min-w-0">
          {tab === 'tasks' && <TasksView />}
          {tab === 'adventure' && <AdventureView />}
          {tab === 'record' && <RecordView character={character.data} />}
          {tab === 'badges' && <BadgesView />}
        </div>
      </main>

      <footer className="mx-auto max-w-6xl px-4 pb-8 text-[11px] text-ink-faint">
        Questward &middot; self-hosted &middot;{' '}
        <a href="/scalar/v1" className="underline decoration-line-strong underline-offset-2 hover:text-ink-muted">
          API reference
        </a>
      </footer>

      <XpFloatLayer />
      <ToastStack />
      <LevelUpOverlay />
    </div>
  )
}
