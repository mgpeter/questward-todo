import { ChevronsUp, Coins, Dices, Timer, Zap } from 'lucide-react'
import { motion } from 'motion/react'
import { useBuyOffer, useInventory, useRerollShop, useShop, useUpgradeItem } from '../../lib/rpgQueries'
import { affixesInForce, type InventoryItem, type Shop as ShopData } from '../../lib/rpg'

export function Shop() {
  const shop = useShop()
  const inventory = useInventory()
  const buy = useBuyOffer()
  const reroll = useRerollShop()

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
            Today's stock, one of each. Nothing here beats what you can win, but it is reliable.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-3">
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

          {shop.data && <RestockButton shop={shop.data} onRestock={() => reroll.mutate()} pending={reroll.isPending} />}
        </div>
      </header>

      {reroll.isError && (
        <p role="alert" className="panel rounded-xl px-4 py-3 text-[12.5px] text-rose">
          {(reroll.error as Error).message}
        </p>
      )}

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
              disabled={offer.soldOut || !offer.affordable || buy.isPending}
              data-testid={`buy-${offer.itemKey}`}
              data-sold-out={offer.soldOut}
              className="tabular mt-3 inline-flex items-center justify-center gap-1.5 rounded-lg bg-ink px-3 py-2 text-xs font-medium text-canvas transition hover:opacity-90 disabled:opacity-30"
            >
              {/* One of each per day. Showing the price on a card that cannot be bought
                  again would read as the shop refusing a purchase it is offering. */}
              {offer.soldOut ? (
                'Bought today'
              ) : (
                <>
                  <Coins size={12} />
                  {offer.price.toLocaleString()}
                </>
              )}
            </button>
          </motion.li>
        ))}
      </ul>

      {upgradeable.length > 0 && <UpgradeBench items={upgradeable} />}
    </div>
  )
}

/**
 * Named for the route it calls, not "Reforge" as it once was. The forge now has a reforge
 * of its own that rerolls affix words for essence, and two buttons meaning different
 * things under one word is how a player spends the wrong currency.
 */
function UpgradeBench({ items }: { items: InventoryItem[] }) {
  const upgrade = useUpgradeItem()

  return (
    <section className="panel rounded-2xl p-5" data-testid="upgrade-bench">
      <h3 className="flex items-center gap-1.5 text-[11px] font-medium uppercase tracking-[0.16em] text-ink-faint">
        <ChevronsUp size={12} />
        Upgrade
      </h3>
      <p className="mt-1 text-[12px] text-ink-muted">
        Pay gold to raise an item one rarity. Any words already on it grow with it, but gold
        never buys a new one. Legendary is as far as it goes.
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
            className={`rarity-${item.rarity} flex flex-wrap items-center gap-3 rounded-xl border border-line p-3`}
            data-testid="upgrade-item"
            data-rarity={item.rarity}
          >
            <span
              aria-hidden="true"
              className="h-7 w-1 shrink-0 rounded-full"
              style={{ backgroundColor: 'var(--tier)' }}
            />

            <span className="min-w-0 flex-1 basis-48">
              <span className="block text-[13.5px]">{item.name}</span>
              <span className="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-1 text-[11px] text-ink-faint">
                <span className="capitalize">{item.rarity}</span>
                {affixesInForce(item) > 0 && (
                  <span className="text-teal">
                    {affixesInForce(item) === 1 ? '1 word' : `${affixesInForce(item)} words`} grow
                    stronger
                  </span>
                )}
              </span>
            </span>

            <button
              type="button"
              onClick={() => upgrade.mutate(item.id)}
              disabled={upgrade.isPending}
              data-testid={`upgrade-${item.itemKey}`}
              className="shrink-0 rounded-lg border border-line px-3 py-1.5 text-[11.5px] text-ink-muted transition hover:border-gold hover:text-gold disabled:opacity-40"
            >
              Upgrade
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}

/**
 * Pays stamina for a whole new shelf.
 *
 * The price is on the button rather than behind a confirmation, because it climbs steeply:
 * the first restock costs one stamina and the seventh costs a thousand. Somebody who cannot
 * see the next number would reasonably expect the second to cost what the first did.
 */
function RestockButton({
  shop,
  onRestock,
  pending,
}: {
  shop: ShopData
  onRestock: () => void
  pending: boolean
}) {
  const spent = shop.nextRerollCost === null
  const affordable = !spent && shop.stamina >= shop.nextRerollCost!

  return (
    <button
      type="button"
      onClick={onRestock}
      disabled={spent || !affordable || pending}
      data-testid="shop-reroll"
      title={
        spent
          ? 'The trader has restocked as often as they are going to today.'
          : affordable
            ? `A whole new shelf for ${shop.nextRerollCost} stamina. ${shop.rerollsLeft} left today.`
            : `Restocking costs ${shop.nextRerollCost} stamina and you have ${shop.stamina}.`
      }
      className="flex items-center gap-1.5 rounded-lg border border-line px-2.5 py-1.5 text-[11.5px] text-ink-muted transition hover:border-gold hover:text-gold disabled:cursor-not-allowed disabled:border-line disabled:text-ink-faint disabled:hover:text-ink-faint"
    >
      <Dices size={12} />
      {spent ? (
        'Restocked out'
      ) : (
        <>
          Restock
          <span className="tabular flex items-center gap-0.5">
            <Zap size={10} />
            {shop.nextRerollCost}
          </span>
        </>
      )}
    </button>
  )
}

function untilRotation(rotatesAt: string): string {
  const ms = new Date(rotatesAt).getTime() - Date.now()

  if (ms <= 0) return 'restocking'

  const hours = Math.floor(ms / 3_600_000)
  const minutes = Math.floor((ms % 3_600_000) / 60_000)

  return hours > 0 ? `restocks in ${hours}h ${minutes}m` : `restocks in ${minutes}m`
}
