import { ChevronDown, ChevronsUp, Coins } from 'lucide-react'
import { useState } from 'react'
import { affixesInForce, type InventoryItem, type UpgradePreview } from '../../lib/rpg'
import { useUpgradeItem } from '../../lib/rpgQueries'

/**
 * Named for the route it calls, not "Reforge" as it once was. The forge now has a reforge
 * of its own that rerolls affix words for essence, and two buttons meaning different
 * things under one word is how a player spends the wrong currency.
 */
export function UpgradeBench({ items, gold }: { items: InventoryItem[]; gold: number }) {
  const upgrade = useUpgradeItem()

  return (
    <section className="panel rounded-2xl p-5" data-testid="upgrade-bench">
      <h3 className="flex items-center gap-1.5 text-[11px] font-medium tracking-[0.16em] text-ink-faint uppercase">
        <ChevronsUp size={12} />
        Upgrade
      </h3>
      <p className="mt-1 text-[12px] text-ink-muted">
        Pay gold to raise an item one rarity. Legendary is as far as it goes, and gold never buys
        a new word - only the forge does that.
      </p>

      <ul className="mt-3 space-y-2">
        {items.map((item) => (
          <UpgradeRow
            key={item.id}
            item={item}
            gold={gold}
            onUpgrade={() => upgrade.mutate(item.id)}
            pending={upgrade.isPending && upgrade.variables === item.id}
            // Scoped to the row that failed. One alert above the whole list meant a refusal
            // about an item you had since scrolled past sat there until the page reloaded.
            error={
              upgrade.isError && upgrade.variables === item.id
                ? (upgrade.error as Error).message
                : null
            }
          />
        ))}
      </ul>
    </section>
  )
}

/**
 * One item, what a step would do to it, and what that costs.
 *
 * The summary answers the decision - which rarity, which stat, what price - and the detail
 * answers the arithmetic. Nine rows of full before-and-after is a page nobody reads, and the
 * name and rarity alone is a page nobody can act on, which is what this was: the only way to
 * learn the price was to press the button and read the refusal.
 */
function UpgradeRow({
  item,
  gold,
  onUpgrade,
  pending,
  error,
}: {
  item: InventoryItem
  gold: number
  onUpgrade: () => void
  pending: boolean
  error: string | null
}) {
  const [open, setOpen] = useState(false)

  // Non-null by construction: the bench is handed only items carrying a preview.
  const next = item.upgrade as UpgradePreview
  const short = next.cost - gold
  const armourMoves = next.armourBonus !== item.armourBonus

  const abilityChanges = next.abilityBonuses.map((bonus) => ({
    label: bonus.label,
    to: bonus.value,
    from: item.abilityBonuses.find((b) => b.label === bonus.label)?.value ?? 0,
  }))

  return (
    <li
      className={`rarity-${item.rarity} rounded-xl border border-line`}
      data-testid="upgrade-item"
      data-rarity={item.rarity}
    >
      <div className="flex flex-wrap items-center gap-3 p-3">
        <span
          aria-hidden="true"
          className="h-7 w-1 shrink-0 rounded-full"
          style={{ backgroundColor: 'var(--tier)' }}
        />

        <button
          type="button"
          onClick={() => setOpen((current) => !current)}
          aria-expanded={open}
          data-testid="upgrade-detail-toggle"
          className="min-w-0 flex-1 basis-48 text-left"
        >
          <span className="flex items-center gap-1.5">
            <span className="min-w-0 truncate text-[13.5px]">{item.name}</span>
            <ChevronDown
              size={12}
              className={`shrink-0 text-ink-faint transition-transform ${open ? 'rotate-180' : ''}`}
            />
          </span>

          <span
            className="tabular mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-1 text-[11px] text-ink-faint"
            data-testid="upgrade-summary"
          >
            <span className="capitalize">
              {item.rarity} &rarr; {next.toRarity}
            </span>

            {armourMoves && (
              <span className="text-teal">
                armour +{item.armourBonus} &rarr; +{next.armourBonus}
              </span>
            )}

            {abilityChanges
              .filter((change) => change.from !== change.to)
              .map((change) => (
                <span key={change.label} className="text-teal">
                  {change.label} +{change.from} &rarr; +{change.to}
                </span>
              ))}

            {/* Only true crossing into Epic. The bench used to promise this on every step. */}
            {next.affixesGrow && affixesInForce(item) > 0 && (
              <span className="text-teal">
                {affixesInForce(item) === 1 ? 'its word doubles' : 'its words double'}
              </span>
            )}
          </span>
        </button>

        <span className="flex shrink-0 flex-col items-end gap-1">
          <button
            type="button"
            onClick={onUpgrade}
            disabled={pending || short > 0}
            title={`Raise it to ${next.toRarity} for ${next.cost} gold`}
            data-testid={`upgrade-${item.itemKey}`}
            className="tabular inline-flex items-center gap-1.5 rounded-lg border border-line px-3 py-1.5 text-[11.5px] text-ink-muted transition hover:border-gold hover:text-gold disabled:opacity-40 disabled:hover:border-line disabled:hover:text-ink-muted"
          >
            <Coins size={11} />
            {next.cost.toLocaleString()}
          </button>

          {/* The shortfall, not a bare refusal. Once you cannot press it, the only thing worth
              knowing is the number you are saving towards. */}
          {short > 0 && (
            <span className="tabular text-[10.5px] text-ink-faint" data-testid="upgrade-short">
              need {short.toLocaleString()} more
            </span>
          )}
        </span>
      </div>

      {open && (
        <dl
          className="tabular grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 border-t border-line px-3 py-2.5 text-[11px]"
          data-testid="upgrade-detail"
        >
          <Change label="Rarity" from={item.rarity} to={next.toRarity} capitalise />

          {armourMoves && (
            <Change label="Armour" from={`+${item.armourBonus}`} to={`+${next.armourBonus}`} />
          )}

          {abilityChanges.map((change) => (
            <Change
              key={change.label}
              label={change.label}
              from={`+${change.from}`}
              to={`+${change.to}`}
            />
          ))}

          {affixesInForce(item) > 0 && (
            <>
              <dt className="text-ink-faint">Words</dt>
              <dd className={next.affixesGrow ? 'text-teal' : 'text-ink-muted'}>
                {[item.prefix, item.suffix].filter(Boolean).join(', ')}
                {next.affixesGrow ? ' — twice as strong' : ' — unchanged'}
              </dd>
            </>
          )}

          {next.affixSlots !== item.affixSlots && (
            <>
              <dt className="text-ink-faint">Slots</dt>
              <dd className="text-ink-muted">
                {item.affixSlots} &rarr; {next.affixSlots}, and only the forge fills one
              </dd>
            </>
          )}

          {/* A weapon's dice never move with rarity - what rarity buys is the ability score
              behind the roll. Saying so stops the absence from reading as a missing stat. */}
          {item.damage && (
            <>
              <dt className="text-ink-faint">Damage</dt>
              <dd className="text-ink-muted">{item.damage}, unchanged by rarity</dd>
            </>
          )}
        </dl>
      )}

      {error && (
        <p role="alert" className="border-t border-line px-3 py-2 text-[11.5px] text-rose">
          {error}
        </p>
      )}
    </li>
  )
}

/** One before-and-after line of the detail panel. */
function Change({
  label,
  from,
  to,
  capitalise = false,
}: {
  label: string
  from: string
  to: string
  capitalise?: boolean
}) {
  return (
    <>
      <dt className="text-ink-faint">{label}</dt>
      <dd className={capitalise ? 'capitalize' : ''}>
        <span className="text-ink-muted">{from}</span>
        <span className="mx-1.5 text-ink-faint">&rarr;</span>
        <span className="text-teal">{to}</span>
      </dd>
    </>
  )
}
