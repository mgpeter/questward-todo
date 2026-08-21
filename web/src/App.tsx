import { useState } from 'react'
import { BottomNav } from './components/BottomNav'
import { CharacterCard } from './components/CharacterCard'
import { Header } from './components/Header'
import { LevelUpOverlay } from './components/LevelUpOverlay'
import { QuickAddSheet } from './components/QuickAdd'
import { ContractSettled } from './components/rpg/ContractSettled'
import { ToastStack } from './components/ToastStack'
import { XpFloatLayer } from './components/XpFloatLayer'
import { useNavigation } from './game/Navigation'
import { useCharacter } from './lib/queries'
import { useIsMobile } from './lib/useMediaQuery'
import { AdventureView } from './views/AdventureView'
import { BadgesView } from './views/BadgesView'
import { RecordView } from './views/RecordView'
import { TasksView } from './views/TasksView'

export default function App() {
  const { tab, setTab } = useNavigation()
  const isMobile = useIsMobile()
  const character = useCharacter()
  const [addOpen, setAddOpen] = useState(false)

  // The medallion is the one thing the compact header already says twice over: level, XP,
  // title and resources all live in the rail and the HUD line now. It stays on Adventure,
  // where it is the subject of the page rather than a preamble to the work.
  const showCharacterCard = !isMobile || tab === 'adventure'

  return (
    <div id="app-shell" className="relative z-10 min-h-dvh">
      <Header
        character={character.data}
        tab={tab}
        onTabChange={setTab}
        badgeCount={character.data?.achievementsUnlocked ?? 0}
      />

      {/*
        Widened from max-w-6xl. That was right for a single-column list, but the board puts
        three columns in the same space: on a 1892px window the shell still capped at
        1152px, which left each column 260px and each card's title 36px.
      */}
      <main
        className={`mx-auto grid max-w-[88rem] gap-5 px-4 py-6 lg:grid-cols-[290px_minmax(0,1fr)] lg:gap-6 ${
          // Clears the fixed bar, and the home indicator behind it.
          isMobile ? 'pb-[calc(5.5rem+env(safe-area-inset-bottom))]' : ''
        }`}
      >
        {showCharacterCard && (
          <aside className="lg:sticky lg:top-32 lg:self-start">
            {character.data ? (
              <CharacterCard character={character.data} />
            ) : (
              <div className="panel h-80 animate-pulse rounded-2xl opacity-60" />
            )}
          </aside>
        )}

        <div className="min-w-0">
          {tab === 'tasks' && <TasksView />}
          {tab === 'adventure' && <AdventureView />}
          {tab === 'record' && <RecordView character={character.data} />}
          {tab === 'badges' && <BadgesView />}
        </div>
      </main>

      {/* The bar owns the bottom of a phone screen, so the colophon would sit under it.
          It is reference material, and /scalar/v1 is not a phone-sized page anyway. */}
      {!isMobile && (
        <footer className="mx-auto max-w-[88rem] px-4 pb-[calc(2rem+env(safe-area-inset-bottom))] text-[11px] text-ink-faint">
          Questward &middot; self-hosted &middot;{' '}
          <a
            href="/scalar/v1"
            className="underline decoration-line-strong underline-offset-2 hover:text-ink-muted"
          >
            API reference
          </a>
        </footer>
      )}

      {/* Reachable from every section, not just the board: the button is in the bar on all
          four. A quest added from the Record tab lands somewhere you cannot see, so the
          create moves you to the board. */}
      {isMobile && (
        <QuickAddSheet
          open={addOpen}
          onClose={() => setAddOpen(false)}
          onCreated={() => {
            setAddOpen(false)
            setTab('tasks')
          }}
        />
      )}

      {isMobile && (
        <BottomNav
          badgeCount={character.data?.achievementsUnlocked ?? 0}
          onAdd={() => setAddOpen(true)}
        />
      )}

      <XpFloatLayer />
      <ToastStack />
      <LevelUpOverlay />
      <ContractSettled />
    </div>
  )
}
