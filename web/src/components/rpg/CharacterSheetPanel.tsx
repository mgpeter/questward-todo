import {
  Coins,
  Gem,
  Heart,
  Moon,
  Recycle,
  RefreshCw,
  Shapes,
  Shield,
  Sparkles,
  Swords,
  Zap,
} from 'lucide-react'
import { motion } from 'motion/react'
import { useState } from 'react'
import {
  affixesInForce,
  canImbue,
  canReforge,
  isConsumable,
  type CharacterSheet,
  type InventoryItem,
  type SetProgress,
} from '../../lib/rpg'
import { useCraftItem, useEquip, useRest, useSalvageItem, useSellItem } from '../../lib/rpgQueries'
import { useIsMobile } from '../../lib/useMediaQuery'

export function CharacterSheetPanel({
  sheet,
  inventory,
  onChangeClass,
}: {
  sheet: CharacterSheet
  inventory: InventoryItem[]
  onChangeClass: () => void
}) {
  const isMobile = useIsMobile()
  const [abilitiesOpen, setAbilitiesOpen] = useState(false)

  return (
    <div className="space-y-5" data-testid="character-sheet">
      <section className="panel rounded-2xl p-5">
        <header className="flex flex-wrap items-baseline justify-between gap-2">
          <div>
            <h2 className="font-display text-2xl">
              {sheet.className ?? 'Unclassed'}
              <span className="ml-2 text-[13px] text-ink-faint">Level {sheet.level}</span>
            </h2>
            {sheet.perk && (
              <p className="mt-1 text-[12.5px] leading-snug text-ink-muted">
                <span className="text-gold">{sheet.perk.name}.</span> {sheet.perk.description}
              </p>
            )}
          </div>

          {!isMobile && (
            <button
              type="button"
              onClick={onChangeClass}
              data-testid="change-class"
              className="rounded-lg border border-line px-3 py-1.5 text-[11.5px] text-ink-muted transition hover:border-gold hover:text-gold"
            >
              {sheet.classKey ? 'Change class' : 'Choose a class'}
            </button>
          )}
        </header>

        {/* Four columns at lg, not five: Recovery spans two, so seven tiles fill 4 + 4 exactly. */}
        <div className="mt-5 grid grid-cols-2 gap-2.5 sm:grid-cols-3 lg:grid-cols-4">
          <Stat icon={<Heart size={13} />} label="Hit points" testId="stat-hp">
            <span className="tabular">
              {sheet.currentHitPoints}
              <span className="text-ink-faint">/{sheet.maxHitPoints}</span>
            </span>
          </Stat>
          <Recovery sheet={sheet} />
          <Stat icon={<Shield size={13} />} label="Armour class" testId="stat-ac">
            <span className="tabular">{sheet.armourClass}</span>
          </Stat>
          <Stat icon={<Swords size={13} />} label="Attack" testId="stat-attack">
            <span className="tabular">
              +{sheet.attackBonus}
              <span className="ml-1 text-[11px] text-ink-faint">{sheet.damage}</span>
            </span>
          </Stat>
          <Stat icon={<Zap size={13} />} label="Stamina" testId="stat-stamina">
            <span className="tabular text-teal">{sheet.stamina}</span>
          </Stat>
          <Stat icon={<Coins size={13} />} label="Gold" testId="stat-gold">
            <span className="tabular text-gold">{sheet.gold.toLocaleString()}</span>
          </Stat>
          <Stat icon={<Gem size={13} />} label="Essence" testId="stat-essence">
            <span className="tabular text-teal">{sheet.essence.toLocaleString()}</span>
          </Stat>
        </div>

        {/* Six tiles of two-digit numbers nobody reads every visit. Behind a disclosure on
            a phone, and always open on a board that has the width for them. */}
        <div
          className={`mt-4 grid grid-cols-3 gap-2 sm:grid-cols-6 ${
            isMobile && !abilitiesOpen ? 'hidden' : ''
          }`}
        >
          {sheet.abilities.map((ability) => (
            <div
              key={ability.key}
              className="rounded-xl border border-line bg-surface-sunk px-2 py-2.5 text-center"
              data-testid={`ability-${ability.abbreviation}`}
            >
              <p className="text-[9px] font-medium uppercase tracking-[0.16em] text-ink-faint">
                {ability.abbreviation}
              </p>
              <p className="tabular mt-1 text-lg leading-none">
                {ability.modifier >= 0 ? '+' : ''}
                {ability.modifier}
              </p>
              <p className="tabular mt-1 text-[10px] text-ink-faint">
                {ability.score}
                {ability.bonusFromItems > 0 && (
                  <span className="text-teal"> (+{ability.bonusFromItems})</span>
                )}
              </p>
            </div>
          ))}
        </div>
        {isMobile && (
          <div className="mt-4 flex gap-2">
            <button
              type="button"
              onClick={onChangeClass}
              data-testid="change-class"
              className="min-h-11 flex-1 rounded-xl bg-ink py-3 text-[12.5px] font-medium text-canvas transition"
            >
              {sheet.classKey ? 'Change class' : 'Choose a class'}
            </button>
            <button
              type="button"
              onClick={() => setAbilitiesOpen((open) => !open)}
              aria-expanded={abilitiesOpen}
              data-testid="toggle-abilities"
              className="min-h-11 flex-1 rounded-xl border border-line py-3 text-[12.5px] font-medium text-ink-muted transition"
            >
              Abilities
            </button>
          </div>
        )}
      </section>

      <Sets sets={sheet.sets} />

      <Inventory items={inventory} essence={sheet.essence} />
    </div>
  )
}

/**
 * Healing was always happening; nothing ever said so, which made the app look broken to
 * anyone watching a bar that did not move. This shows the clock, and sells the bed.
 */
function Recovery({ sheet }: { sheet: CharacterSheet }) {
  const rest = useRest()
  const whole = sheet.currentHitPoints >= sheet.maxHitPoints

  return (
    <div
      className="col-span-2 rounded-xl border border-line bg-surface-sunk px-3 py-2.5"
      data-testid="recovery"
    >
      <p className="flex items-center gap-1.5 text-[9.5px] font-medium uppercase tracking-[0.14em] text-ink-faint">
        <Moon size={13} />
        Recovery
      </p>

      {whole ? (
        <p className="mt-1.5 text-[12.5px] text-teal">In fighting shape.</p>
      ) : (
        <>
          <p className="mt-1.5 text-[12px] leading-snug text-ink-muted">
            <span className="text-ink">+1 HP {relative(sheet.nextRegenerationAt)}</span>
            {sheet.fullyHealedAt && (
              <span className="text-ink-faint"> · full {relative(sheet.fullyHealedAt)}</span>
            )}
          </p>

          <button
            type="button"
            onClick={() => rest.mutate()}
            disabled={rest.isPending || sheet.gold < sheet.restCost}
            title={`Sleep at the tavern for ${sheet.restCost} gold`}
            data-testid="rest"
            className="tabular mt-2 w-full rounded-lg border border-line px-2.5 py-1.5 text-[11.5px] text-ink-muted transition hover:border-gold hover:text-gold disabled:opacity-40"
          >
            Rest for {sheet.restCost} gold
          </button>
        </>
      )}

      {rest.isError && (
        <p role="alert" className="mt-1.5 text-[11px] text-rose">
          {(rest.error as Error).message}
        </p>
      )}
    </div>
  )
}

/** "in 6m" / "in 2h 10m", from an absolute instant. */
function relative(at: string | null): string {
  if (!at) return 'soon'

  const ms = new Date(at).getTime() - Date.now()

  if (ms <= 0) return 'any moment'

  const minutes = Math.round(ms / 60_000)

  if (minutes < 60) return `in ${Math.max(1, minutes)}m`

  return `in ${Math.floor(minutes / 60)}h ${minutes % 60}m`
}

function Stat({
  icon,
  label,
  children,
  testId,
}: {
  icon: React.ReactNode
  label: string
  children: React.ReactNode
  testId: string
}) {
  return (
    <div className="rounded-xl border border-line bg-surface-sunk px-3 py-2.5" data-testid={testId}>
      <p className="flex items-center gap-1.5 text-[9.5px] font-medium uppercase tracking-[0.14em] text-ink-faint">
        {icon}
        {label}
      </p>
      <p className="mt-1.5 text-[17px] leading-none font-medium">{children}</p>
    </div>
  )
}

/**
 * The sheet only lists sets the wearer already holds a piece of, so an empty list means
 * "no set pieces worn" rather than "no sets exist". Rendering nothing is correct: the
 * other sets are advertised on the pieces themselves, in the inventory below.
 */
function Sets({ sets }: { sets: SetProgress[] }) {
  if (sets.length === 0) return null

  return (
    <section className="panel rounded-2xl p-5" data-testid="sets">
      <h3 className="mb-3 flex items-center gap-1.5 text-[11px] font-medium uppercase tracking-[0.16em] text-ink-faint">
        <Shapes size={12} />
        Sets
      </h3>

      <ul className="grid gap-2.5 lg:grid-cols-2">
        {sets.map((set) => {
          const complete = set.equipped >= set.total

          return (
            <motion.li
              key={set.key}
              layout
              className="rounded-xl border border-line bg-surface-sunk p-3"
              data-testid="set"
              data-set={set.key}
              data-complete={complete}
            >
              <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
                <p className="min-w-0 text-[13.5px]">{set.name}</p>
                <p
                  className={`tabular shrink-0 text-[11px] ${complete ? 'text-gold' : 'text-ink-faint'}`}
                  data-testid="set-count"
                >
                  {set.equipped} of {set.total}
                </p>
              </div>

              <p className="mt-1 text-[11.5px] leading-snug text-ink-muted">{set.blurb}</p>

              <ul className="mt-2 space-y-1">
                {set.tiers.map((tier) => (
                  <li
                    key={tier.pieces}
                    className={`flex items-baseline gap-2 text-[11px] ${
                      tier.active ? 'text-teal' : 'text-ink-faint'
                    }`}
                    data-testid="set-tier"
                    data-pieces={tier.pieces}
                    data-active={tier.active}
                  >
                    <span className="tabular shrink-0">{tier.pieces} pc</span>
                    <span className="min-w-0">{tier.description}</span>
                  </li>
                ))}
              </ul>
            </motion.li>
          )
        })}
      </ul>
    </section>
  )
}

/** Every action button in the inventory row wears the same shape. */
const ACTION =
  'tabular inline-flex shrink-0 items-center gap-1 whitespace-nowrap rounded-lg border border-line px-2.5 py-1.5 text-[11px] transition disabled:opacity-40'

function Inventory({ items, essence }: { items: InventoryItem[]; essence: number }) {
  const equip = useEquip()
  const sell = useSellItem()
  const salvage = useSalvageItem()
  const craft = useCraftItem()

  if (items.length === 0) {
    return (
      <section className="panel rounded-2xl px-5 py-10 text-center" data-testid="inventory-empty">
        <p className="font-display text-lg">Nothing but pocket lint</p>
        <p className="mt-1 text-[13px] text-ink-muted">Win a fight to find something worth carrying.</p>
      </section>
    )
  }

  // The forge is the only place a refusal carries a sentence worth reading ("There is no
  // room on it for another word"), so it gets a visible line rather than a silent no-op.
  //
  // Only the most recently submitted of the two speaks. Each mutation holds its last
  // result forever, so without this a failed salvage would keep printing its refusal
  // underneath the success line of the craft that came after it.
  const spoke = craft.submittedAt >= salvage.submittedAt ? craft : salvage
  const refusal = spoke.error as Error | null

  return (
    <section className="panel rounded-2xl p-5" data-testid="inventory">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-[11px] font-medium uppercase tracking-[0.16em] text-ink-faint">
          Inventory
        </h3>
        <p className="tabular flex items-center gap-1.5 text-[11.5px] text-teal" data-testid="inventory-essence">
          <Gem size={12} />
          {essence.toLocaleString()} essence
        </p>
      </div>

      {refusal && (
        <p role="alert" className="mb-2 text-[12px] text-rose" data-testid="forge-error">
          {refusal.message}
        </p>
      )}

      {/* Equipping had no error line at all, which is how a swap that was failing every
          other time read as a dead button rather than as a bug. */}
      {equip.isError && (
        <p role="alert" className="mb-2 text-[12px] text-rose" data-testid="equip-error">
          {(equip.error as Error).message}
        </p>
      )}

      {spoke === craft && craft.isSuccess && craft.data && (
        <p className="mb-2 flex flex-wrap items-baseline gap-x-2 text-[12px] text-teal" data-testid="forge-result">
          <Sparkles size={12} className="shrink-0 self-center" />
          <span>{craft.data.item.name}</span>
          <span className="tabular text-ink-faint">-{craft.data.essenceSpent} essence</span>
        </p>
      )}

      {spoke === salvage && salvage.isSuccess && salvage.data && (
        <p className="tabular mb-2 text-[12px] text-teal" data-testid="salvage-result">
          Broke it down for {salvage.data.essenceGained} essence.
        </p>
      )}

      <ul className="space-y-2">
        {items.map((item) => {
          const free = item.affixSlots - affixesInForce(item)
          // A consumable is a stack rather than a thing: it is never worn, and selling or
          // breaking one down spends a single unit off the row instead of the whole pile.
          const usable = isConsumable(item)
          const one = item.quantity > 1 ? ' one of them' : ''

          return (
            <motion.li
              key={item.id}
              layout
              className={`rarity-${item.rarity} flex flex-wrap items-center gap-3 rounded-xl border p-3 ${
                item.isEquipped ? 'border-gold/40 bg-gold/6' : 'border-line'
              }`}
              data-testid="inventory-item"
              data-rarity={item.rarity}
              data-equipped={item.isEquipped}
              data-set={item.setName ?? undefined}
            >
              <span
                aria-hidden="true"
                className="h-8 w-1 shrink-0 rounded-full"
                style={{ backgroundColor: 'var(--tier)' }}
              />

              <div className="min-w-0 flex-1 basis-56">
                <p className="flex flex-wrap items-center gap-2 text-[14px]">
                  <span data-testid="item-name">{item.name}</span>
                  <span className="tier-chip rounded-full px-2 py-0.5 text-[10px] font-medium capitalize">
                    {item.rarity}
                  </span>
                  {item.quantity > 1 && (
                    <span
                      className="tabular rounded-full border border-line px-2 py-0.5 text-[10px] font-medium whitespace-nowrap text-ink-muted"
                      data-testid="item-quantity"
                    >
                      x{item.quantity}
                    </span>
                  )}
                  {item.isEquipped && (
                    <span className="text-[10px] font-medium uppercase tracking-[0.14em] text-gold">
                      Equipped
                    </span>
                  )}
                </p>

                <p className="tabular mt-1 flex flex-wrap gap-2 text-[11px] text-ink-faint">
                  <span className="capitalize">{item.slot}</span>
                  {item.damage && <span>{item.damage}</span>}
                  {item.armourBonus > 0 && <span>+{item.armourBonus} AC</span>}
                  {item.abilityBonuses.map((b) => (
                    <span key={b.label} className="text-teal">
                      +{b.value} {b.label}
                    </span>
                  ))}
                </p>

                {usable && (
                  <p className="mt-1 text-[11.5px] leading-snug text-teal" data-testid="item-use">
                    {item.useDescription}
                  </p>
                )}

                {(item.affixSlots > 0 || item.setName) && (
                  <p className="mt-1.5 flex flex-wrap items-center gap-1.5 text-[10px]">
                    {item.prefix && <Affix word={item.prefix} />}
                    {item.suffix && <Affix word={item.suffix} />}
                    {free > 0 && (
                      <span
                        className="whitespace-nowrap rounded-full border border-dashed border-line-strong px-2 py-0.5 text-ink-faint"
                        data-testid="item-affix-empty"
                      >
                        {free} empty {free === 1 ? 'slot' : 'slots'}
                      </span>
                    )}
                    {item.setName && (
                      <span
                        className="whitespace-nowrap rounded-full border border-line px-2 py-0.5 text-ink-muted"
                        data-testid="item-set"
                      >
                        {item.setName} set
                      </span>
                    )}
                  </p>
                )}
              </div>

              <div className="flex flex-wrap gap-1.5">
                {/* Nothing wears a potion, and the server refuses the call. A button that
                    could only ever produce an error is worse than no button. */}
                {!usable && (
                  <button
                    type="button"
                    onClick={() => equip.mutate({ id: item.id, equip: !item.isEquipped })}
                    disabled={equip.isPending}
                    data-testid={item.isEquipped ? 'unequip' : 'equip'}
                    className={`${ACTION} text-ink-muted hover:border-gold hover:text-gold`}
                  >
                    {item.isEquipped ? 'Remove' : 'Equip'}
                  </button>
                )}

                {canImbue(item) && (
                  <button
                    type="button"
                    onClick={() => craft.mutate({ id: item.id, verb: 'imbue' })}
                    disabled={craft.isPending || essence < item.imbueCost}
                    title={`Roll a new word onto it for ${item.imbueCost} essence`}
                    data-testid="imbue"
                    className={`${ACTION} text-ink-muted hover:border-teal hover:text-teal`}
                  >
                    <Sparkles size={11} />
                    {item.imbueCost}
                  </button>
                )}

                {canReforge(item) && (
                  <button
                    type="button"
                    onClick={() => craft.mutate({ id: item.id, verb: 'reforge' })}
                    disabled={craft.isPending || essence < item.reforgeCost}
                    title={`Reroll every word on it for ${item.reforgeCost} essence`}
                    data-testid="reforge"
                    className={`${ACTION} text-ink-muted hover:border-teal hover:text-teal`}
                  >
                    <RefreshCw size={11} />
                    {item.reforgeCost}
                  </button>
                )}

                {!item.isEquipped && (
                  <button
                    type="button"
                    onClick={() => sell.mutate(item.id)}
                    disabled={sell.isPending}
                    title={`Sell${one} for ${item.sellValue} gold`}
                    data-testid="sell"
                    className={`${ACTION} text-ink-faint hover:border-gold hover:text-gold`}
                  >
                    Sell {item.sellValue}
                  </button>
                )}

                {!item.isEquipped && (
                  <button
                    type="button"
                    onClick={() => salvage.mutate(item.id)}
                    disabled={salvage.isPending}
                    title={`Break${one ? ' one' : ' it'} down for ${item.salvageValue} essence. It is destroyed.`}
                    data-testid="salvage"
                    className={`${ACTION} text-ink-faint hover:border-rose hover:text-rose`}
                  >
                    <Recycle size={11} />
                    {item.salvageValue}
                  </button>
                )}
              </div>
            </motion.li>
          )
        })}
      </ul>
    </section>
  )
}

function Affix({ word }: { word: string }) {
  return (
    <span
      className="whitespace-nowrap rounded-full border border-teal/35 bg-teal/8 px-2 py-0.5 text-teal"
      data-testid="item-affix"
    >
      {word}
    </span>
  )
}
