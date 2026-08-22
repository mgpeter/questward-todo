import { Coins, Dices, Timer, Zap } from 'lucide-react'
import { motion } from 'motion/react'
import { useBuyOffer, useInventory, useRerollShop, useShop } from '../../lib/rpgQueries'
import type { Shop as ShopData } from '../../lib/rpg'
import { ItemStats } from './HuntChrome'
import { UpgradeBench } from './UpgradeBench'

export function Shop() {
  const shop = useShop()
  const inventory = useInventory()
  const buy = useBuyOffer()
  const reroll = useRerollShop()

  if (shop.isLoading) {
    return <div className="panel h-72 animate-pulse rounded-2xl opacity-60" />
  }

  // The server decides. It used to be `rarity !== 'legendary'` here, which offered potions the
  // bench hard-refuses, because the consumable rule lived only on the server.
  const upgradeable = (inventory.data ?? []).filter((i) => i.upgrade !== null)

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

            <ItemStats item={offer} size="small" className="mt-2" />

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

      {upgradeable.length > 0 && (
        <UpgradeBench items={upgradeable} gold={shop.data?.gold ?? 0} />
      )}
    </div>
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
