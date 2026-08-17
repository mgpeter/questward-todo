import { Coins, Hammer, Timer } from 'lucide-react'
import { motion } from 'motion/react'
import { useBuyOffer, useInventory, useShop, useUpgradeItem } from '../../lib/rpgQueries'
import type { InventoryItem } from '../../lib/rpg'

export function Shop() {
  const shop = useShop()
  const inventory = useInventory()
  const buy = useBuyOffer()

  if (shop.isLoading) {
    return <div className="panel h-72 animate-pulse rounded-2xl opacity-60" />
  }

  const upgradeable = (inventory.data ?? []).filter((i) => i.rarity !== 'legendary')

  return (
    <div className="space-y-5" data-testid="shop">
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <h2 className="font-display text-2xl">The Market</h2>
          <p className="mt-0.5 text-[13px] text-ink-muted">
            Today's stock. Nothing here beats what you can win, but it is reliable.
          </p>
        </div>

        <p className="tabular flex items-center gap-3 text-[12px]">
          <span className="flex items-center gap-1.5 text-gold">
            <Coins size={12} />
            {shop.data?.gold.toLocaleString()}
          </span>
          {shop.data && (
            <span className="flex items-center gap-1.5 text-ink-faint">
              <Timer size={12} />
              {untilRotation(shop.data.rotatesAt)}
            </span>
          )}
        </p>
      </header>

      {buy.isError && (
        <p role="alert" className="panel rounded-xl px-4 py-3 text-[12.5px] text-rose">
          {(buy.error as Error).message}
        </p>
      )}

      <ul className="grid gap-2.5 sm:grid-cols-2 xl:grid-cols-3">
        {shop.data?.offers.map((offer, index) => (
          <motion.li
            key={offer.offerId}
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: Math.min(index * 0.04, 0.25) }}
            className={`rarity-${offer.rarity} panel flex flex-col rounded-xl p-4`}
            data-testid="shop-offer"
            data-rarity={offer.rarity}
          >
            <div className="flex items-baseline justify-between gap-2">
              <h3 className="text-[15px]">{offer.name}</h3>
              <span className="tier-chip rounded-full px-2 py-0.5 text-[10px] font-medium capitalize">
                {offer.rarity}
              </span>
            </div>

            <p className="mt-1 flex-1 text-[12px] leading-snug text-ink-muted">{offer.blurb}</p>

            <p className="tabular mt-2 flex flex-wrap gap-2 text-[10.5px] text-ink-faint">
              <span className="capitalize">{offer.slot}</span>
              {offer.damage && <span>{offer.damage}</span>}
              {offer.armourBonus > 0 && <span>+{offer.armourBonus} AC</span>}
              {offer.abilityBonuses.map((b) => (
                <span key={b.label} className="text-teal">
                  +{b.value} {b.label}
                </span>
              ))}
            </p>

            <button
              type="button"
              onClick={() => buy.mutate(offer.offerId)}
              disabled={!offer.affordable || buy.isPending}
              data-testid={`buy-${offer.itemKey}`}
              className="tabular mt-3 inline-flex items-center justify-center gap-1.5 rounded-lg bg-ink px-3 py-2 text-xs font-medium text-canvas transition hover:opacity-90 disabled:opacity-30"
            >
              <Coins size={12} />
              {offer.price.toLocaleString()}
            </button>
          </motion.li>
        ))}
      </ul>

      {upgradeable.length > 0 && <Reforge items={upgradeable} />}
    </div>
  )
}

function Reforge({ items }: { items: InventoryItem[] }) {
  const upgrade = useUpgradeItem()

  return (
    <section className="panel rounded-2xl p-5" data-testid="reforge">
      <h3 className="flex items-center gap-1.5 text-[11px] font-medium uppercase tracking-[0.16em] text-ink-faint">
        <Hammer size={12} />
        Reforge
      </h3>
      <p className="mt-1 text-[12px] text-ink-muted">
        Pay to raise an item one rarity. Legendary is as far as it goes.
      </p>

      {upgrade.isError && (
        <p role="alert" className="mt-2 text-[12px] text-rose">
          {(upgrade.error as Error).message}
        </p>
      )}

      <ul className="mt-3 space-y-2">
        {items.map((item) => (
          <li
            key={item.id}
            className={`rarity-${item.rarity} flex items-center gap-3 rounded-xl border border-line p-3`}
            data-testid="reforge-item"
          >
            <span
              aria-hidden="true"
              className="h-7 w-1 shrink-0 rounded-full"
              style={{ backgroundColor: 'var(--tier)' }}
            />

            <span className="min-w-0 flex-1">
              <span className="block text-[13.5px]">{item.name}</span>
              <span className="block text-[11px] capitalize text-ink-faint">{item.rarity}</span>
            </span>

            <button
              type="button"
              onClick={() => upgrade.mutate(item.id)}
              disabled={upgrade.isPending}
              data-testid={`upgrade-${item.itemKey}`}
              className="shrink-0 rounded-lg border border-line px-3 py-1.5 text-[11.5px] text-ink-muted transition hover:border-gold hover:text-gold disabled:opacity-40"
            >
              Reforge
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}

function untilRotation(rotatesAt: string): string {
  const ms = new Date(rotatesAt).getTime() - Date.now()

  if (ms <= 0) return 'restocking'

  const hours = Math.floor(ms / 3_600_000)
  const minutes = Math.floor((ms % 3_600_000) / 60_000)

  return hours > 0 ? `restocks in ${hours}h ${minutes}m` : `restocks in ${minutes}m`
}
