import { FlaskRound } from 'lucide-react'
import { motion } from 'motion/react'
import { usableConsumables, type InventoryItem } from '../../lib/rpg'

/**
 * The potions and pellets to hand, in the fight where they are the only place they can be
 * used.
 *
 * A row is a stack, so the count is the row's quantity rather than a tally of rows. The
 * server removes a stack once the last one goes, which means an empty stack is normally
 * invisible; the disabled-at-zero branch still exists because the bag is refetched after
 * the round rather than during it, and a tray that let a click through in that window
 * would spend a potion the player does not have.
 */
export function ConsumableTray({
  items,
  onUse,
  disabled,
  pendingId,
}: {
  items: InventoryItem[]
  onUse: (item: InventoryItem) => void
  disabled: boolean
  pendingId: string | null
}) {
  const tray = usableConsumables(items)

  if (tray.length === 0) {
    return (
      <div
        className="mt-4 rounded-xl border border-dashed border-line px-3 py-2.5 text-[11.5px] text-ink-faint"
        data-testid="consumable-tray-empty"
      >
        Nothing to drink or throw. The Market keeps one on the shelf every day.
      </div>
    )
  }

  return (
    <div className="mt-4" data-testid="consumable-tray" data-count={tray.length}>
      <p className="mb-1.5 flex items-center gap-1.5 text-[9.5px] font-medium uppercase tracking-[0.16em] text-ink-faint">
        <FlaskRound size={12} />
        Satchel
      </p>

      {/* One column until there is room for two: a use description is a whole sentence and
          two of them side by side in the narrow shell would each be four lines tall. */}
      <ul className="grid gap-1.5 sm:grid-cols-2">
        {tray.map((item) => {
          const empty = item.quantity <= 0

          return (
            <motion.li key={item.id} layout className={`rarity-${item.rarity}`}>
              <button
                type="button"
                onClick={() => onUse(item)}
                disabled={disabled || empty}
                title={item.useDescription ?? undefined}
                data-testid="consumable"
                data-item={item.itemKey}
                data-rarity={item.rarity}
                data-quantity={item.quantity}
                className="tier-chip flex w-full items-start gap-2 rounded-lg px-2.5 py-2 text-left transition hover:brightness-105 disabled:border-line disabled:bg-transparent disabled:text-ink-faint disabled:opacity-60"
              >
                <span className="min-w-0 flex-1">
                  <span className="block text-[12px] font-medium">{item.name}</span>
                  <span className="mt-0.5 block text-[10.5px] leading-snug opacity-80">
                    {empty ? 'None left' : (item.useDescription ?? '')}
                  </span>
                </span>

                {/* Tinted from the rarity the row already carries, so the count reads as
                    part of the chip rather than as a second border colour. */}
                <span
                  className="tabular mt-0.5 shrink-0 whitespace-nowrap rounded-full border px-1.5 py-0.5 text-[10px] font-medium"
                  style={{ borderColor: 'color-mix(in srgb, currentColor 34%, transparent)' }}
                  data-testid="consumable-count"
                >
                  {pendingId === item.id ? '...' : `x${item.quantity}`}
                </span>
              </button>
            </motion.li>
          )
        })}
      </ul>
    </div>
  )
}
