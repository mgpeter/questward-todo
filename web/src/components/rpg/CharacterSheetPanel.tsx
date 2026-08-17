import { Coins, Heart, Moon, Shield, Swords, Zap } from 'lucide-react'
import { motion } from 'motion/react'
import type { CharacterSheet, InventoryItem } from '../../lib/rpg'
import { useEquip, useRest, useSellItem } from '../../lib/rpgQueries'

export function CharacterSheetPanel({
  sheet,
  inventory,
  onChangeClass,
}: {
  sheet: CharacterSheet
  inventory: InventoryItem[]
  onChangeClass: () => void
}) {
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

          <button
            type="button"
            onClick={onChangeClass}
            data-testid="change-class"
            className="rounded-lg border border-line px-3 py-1.5 text-[11.5px] text-ink-muted transition hover:border-gold hover:text-gold"
          >
            {sheet.classKey ? 'Change class' : 'Choose a class'}
          </button>
        </header>

        <div className="mt-5 grid grid-cols-2 gap-2.5 sm:grid-cols-3 lg:grid-cols-5">
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
        </div>

        <div className="mt-4 grid grid-cols-3 gap-2 sm:grid-cols-6">
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
      </section>

      <Inventory items={inventory} />
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
      className="col-span-2 rounded-xl border border-line bg-surface-sunk px-3 py-2.5 sm:col-span-1 lg:col-span-2"
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

function Inventory({ items }: { items: InventoryItem[] }) {
  const equip = useEquip()
  const sell = useSellItem()

  if (items.length === 0) {
    return (
      <section className="panel rounded-2xl px-5 py-10 text-center" data-testid="inventory-empty">
        <p className="font-display text-lg">Nothing but pocket lint</p>
        <p className="mt-1 text-[13px] text-ink-muted">Win a fight to find something worth carrying.</p>
      </section>
    )
  }

  return (
    <section className="panel rounded-2xl p-5" data-testid="inventory">
      <h3 className="mb-3 text-[11px] font-medium uppercase tracking-[0.16em] text-ink-faint">
        Inventory
      </h3>

      <ul className="space-y-2">
        {items.map((item) => (
          <motion.li
            key={item.id}
            layout
            className={`rarity-${item.rarity} flex flex-wrap items-center gap-3 rounded-xl border p-3 ${
              item.isEquipped ? 'border-gold/40 bg-gold/6' : 'border-line'
            }`}
            data-testid="inventory-item"
            data-rarity={item.rarity}
            data-equipped={item.isEquipped}
          >
            <span
              aria-hidden="true"
              className="h-8 w-1 shrink-0 rounded-full"
              style={{ backgroundColor: 'var(--tier)' }}
            />

            <div className="min-w-0 flex-1">
              <p className="flex flex-wrap items-center gap-2 text-[14px]">
                {item.name}
                <span className="tier-chip rounded-full px-2 py-0.5 text-[10px] font-medium capitalize">
                  {item.rarity}
                </span>
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
            </div>

            <div className="flex shrink-0 gap-1.5">
              <button
                type="button"
                onClick={() => equip.mutate({ id: item.id, equip: !item.isEquipped })}
                disabled={equip.isPending}
                data-testid={item.isEquipped ? 'unequip' : 'equip'}
                className="rounded-lg border border-line px-2.5 py-1.5 text-[11px] text-ink-muted transition hover:border-gold hover:text-gold disabled:opacity-40"
              >
                {item.isEquipped ? 'Remove' : 'Equip'}
              </button>

              {!item.isEquipped && (
                <button
                  type="button"
                  onClick={() => sell.mutate(item.id)}
                  disabled={sell.isPending}
                  title={`Sell for ${item.sellValue} gold`}
                  data-testid="sell"
                  className="tabular rounded-lg border border-line px-2.5 py-1.5 text-[11px] text-ink-faint transition hover:border-rose hover:text-rose disabled:opacity-40"
                >
                  Sell {item.sellValue}
                </button>
              )}
            </div>
          </motion.li>
        ))}
      </ul>
    </section>
  )
}
