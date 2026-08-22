import { Coins, Flag, GitFork, Gem, Rat, ShieldHalf, Skull, Waypoints } from 'lucide-react'
import type { ComponentType } from 'react'
import {
  bountyIsCapped,
  bountyLabel,
  bountyTier,
  standingLabel,
  type BountyTier,
  type FactionStandingName,
  type Hunt,
  type HuntContract,
  type HuntOffer,
} from '../../lib/rpg'

/**
 * The five shapes a task can take when it stands up as a monster.
 *
 * Keyed off the archetype the server froze onto the contract, never off the task: the whole
 * point of freezing it is that a task edited after the contract was written still fights as
 * whatever it was written as. Everything else about the creature (its name, its blurb, its
 * numbers) arrives on the wire, so this is the only piece of the catalog the client holds,
 * and it holds it because an SVG cannot be sent through a JSON field.
 */
const ARCHETYPE_ICONS: Record<string, ComponentType<{ size?: number; className?: string }>> = {
  'hunt-drudge': Rat,
  'hunt-tangle': Waypoints,
  'hunt-bulwark': ShieldHalf,
  'hunt-hydra': GitFork,
  'hunt-dread': Skull,
}

export function ArchetypeIcon({
  archetypeKey,
  size = 13,
  className,
}: {
  archetypeKey: string
  size?: number
  className?: string
}) {
  // An unknown key is a catalog the client has not caught up with, not an error worth
  // showing: something still rises out of the task, it is just shaped like a skull.
  const Icon = ARCHETYPE_ICONS[archetypeKey] ?? Skull

  return <Icon size={size} className={className} />
}

/**
 * How loud a contract is allowed to be, by age.
 *
 * Four steps up and no step down. There is no red here and there is nothing on this scale
 * that reads as a warning, because DEC-013 makes an overdue task a bounty rather than a
 * debuff: the oldest thing on the board is the richest thing on the board and it is dressed
 * like it.
 */
const BOUNTY_TONE: Record<BountyTier, string> = {
  none: 'border-line text-ink-muted',
  fresh: 'border-gold/25 bg-gold/6 text-ink-muted',
  rich: 'border-gold/45 bg-gold/12 text-gold',
  legend: 'border-gold/70 bg-gold/20 text-gold font-medium',
}

const bountyToneClass = (daysOverdue: number): string => BOUNTY_TONE[bountyTier(daysOverdue)]

/**
 * The multiplier, wherever a purse is quoted.
 *
 * Nothing here ever subtracts, so it is written as a multiplier and never as a penalty: the
 * number is at worst 1x and at best 2x, and the card that carries it is the reward line.
 */
export function BountyChip({
  bountyPercent,
  daysOverdue,
  testId = 'bounty',
}: {
  bountyPercent: number
  daysOverdue: number
  testId?: string
}) {
  const capped = bountyIsCapped(bountyPercent)

  return (
    <span
      data-testid={testId}
      data-bounty={bountyPercent}
      title={
        capped
          ? 'The bounty is at its cap. Waiting longer pays nothing more, and the fight only gets harder.'
          : 'The gold this contract pays is multiplied by its age, up to twice.'
      }
      className={`tabular inline-flex items-center gap-1 whitespace-nowrap rounded-full border px-2 py-0.5 text-[10px] ${bountyToneClass(daysOverdue)}`}
    >
      <Coins size={9} className="shrink-0" />
      {bountyLabel(bountyPercent)} gold
    </span>
  )
}

/**
 * The banner a contract flies, and how well it knows the hunter.
 *
 * Standing is a record of contracts won, not a balance to spend, so it is shown as a title
 * rather than as a number with a bar under it wherever there is only room for one of them.
 */
export function FactionChip({
  name,
  title,
  standing,
  testId = 'faction',
}: {
  name: string
  title: string | null
  standing: FactionStandingName
  testId?: string
}) {
  return (
    <span
      data-testid={testId}
      data-standing={standing}
      title={title ? `${name}: ${title} (${standingLabel(standing)})` : name}
      className="inline-flex min-w-0 items-center gap-1 rounded-full border border-line bg-surface-sunk px-2 py-0.5 text-[10px] text-ink-muted"
    >
      <Flag size={9} className="shrink-0" />
      <span className="truncate">{name}</span>
    </span>
  )
}

/** The floor a contract reward cannot roll below, which is the one thing standing buys. */
export function RewardFloorChip({ floor, testId = 'reward-floor' }: { floor: string; testId?: string }) {
  return (
    <span
      data-testid={testId}
      data-rarity={floor}
      title="Winning an overdue contract hands over an item from the banner's own table, no worse than this."
      className={`rarity-${floor} tier-chip inline-flex items-center gap-1 whitespace-nowrap rounded-full px-2 py-0.5 text-[10px] capitalize`}
    >
      <Gem size={9} className="shrink-0" />
      {floor}+
    </span>
  )
}

/**
 * What a contract was written on, shown above the fight it bought.
 *
 * The fight itself is an ordinary encounter on the ordinary encounter screen, exactly as a
 * dungeon room is. This is the framing that tells the player which of their own tasks is
 * currently swinging at them, and it is the only place the task's own words appear: the
 * creature's name, the combat log and the chronicle are all catalog text.
 *
 * There is no claim button on it and there is nothing left to claim. A fight only opens on a
 * contract whose task is already finished, so by the time this is on screen the work is done
 * and the purse is what winning pays.
 */
export function HuntBanner({ hunt }: { hunt: Hunt }) {
  return (
    <div
      className="panel rounded-xl px-4 py-3"
      data-testid="hunt-banner"
      data-archetype={hunt.archetypeKey}
    >
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
        <p className="flex min-w-0 items-center gap-1.5 text-[12.5px]">
          <ArchetypeIcon archetypeKey={hunt.archetypeKey} className="shrink-0 text-gold" />
          <span className="min-w-0 break-words">
            {hunt.taskTitle ? (
              <>
                Contract on <span className="font-medium">{hunt.taskTitle}</span>
              </>
            ) : (
              // The task can be deleted mid-fight. The contract survives it whole, because
              // every number it is worth was frozen when it was taken.
              'A contract whose task is gone'
            )}
          </span>
        </p>

        <div className="ml-auto flex flex-wrap items-center gap-1.5">
          <BountyChip bountyPercent={hunt.bountyPercent} daysOverdue={hunt.daysOverdue} />
          {hunt.factionName && (
            <FactionChip
              name={hunt.factionName}
              title={hunt.factionTitle}
              standing={hunt.standing}
            />
          )}
        </div>
      </div>

      {/* The one thing a player can get wrong here, said before the first swing. The work is
          already done and already paid its XP; this is the bounty on top, and no amount of
          fighting has ever granted a point of experience. */}
      <p className="mt-2 text-[11px] leading-snug text-ink-faint" data-testid="hunt-rule">
        The task is finished and the XP is banked. Winning pays the bounty on top, and grants
        no experience.
      </p>
    </div>
  )
}

/** The stat block, quoted the way the tavern quotes a monster's. */
export function OfferStats({ offer }: { offer: HuntOffer }) {
  return (
    <div className="tabular mt-2.5 flex flex-wrap gap-x-2 gap-y-1 text-[10.5px] text-ink-faint">
      <span className="whitespace-nowrap">AC {offer.armourClass}</span>
      <span className="whitespace-nowrap">{offer.maxHitPoints} HP</span>
      <span className="whitespace-nowrap">{offer.damage}</span>
      <span className="whitespace-nowrap text-gold">
        {offer.minGold}-{offer.maxGold} gold
      </span>
      <span className="whitespace-nowrap">{offer.dropChance}% drop</span>
    </div>
  )
}

/** The same block, for a contract that has not opened a fight yet. */
export function ContractStats({ contract }: { contract: HuntContract }) {
  return (
    <div className="tabular mt-2.5 flex flex-wrap gap-x-2 gap-y-1 text-[10.5px] text-ink-faint">
      <span className="whitespace-nowrap">AC {contract.armourClass}</span>
      <span className="whitespace-nowrap">{contract.maxHitPoints} HP</span>
      <span className="whitespace-nowrap">{contract.damage}</span>
      <span className="whitespace-nowrap text-gold">
        {contract.minGold}-{contract.maxGold} gold
      </span>
      <span className="whitespace-nowrap">{contract.dropChance}% drop</span>
    </div>
  )
}

/**
 * Slot, dice, armour and ability bonuses on one wrapping line.
 *
 * Structurally typed rather than taking an InventoryItem, because the shop's offers carry the
 * same four fields without being inventory rows. This existed twice already, byte for byte, in
 * the bag and in the shop; the upgrade bench wanting it a third time is what made it a
 * component. `size` is the only thing the two copies actually disagreed about.
 */
export function ItemStats({
  item,
  size = 'normal',
  className = '',
}: {
  item: {
    slot: string
    damage: string | null
    armourBonus: number
    abilityBonuses: { label: string; value: number }[]
  }
  size?: 'normal' | 'small'
  className?: string
}) {
  return (
    <p
      className={`tabular flex flex-wrap gap-2 text-ink-faint ${
        size === 'small' ? 'text-[10.5px]' : 'text-[11px]'
      } ${className}`}
    >
      <span className="capitalize">{item.slot}</span>
      {item.damage && <span>{item.damage}</span>}
      {item.armourBonus > 0 && <span>+{item.armourBonus} AC</span>}
      {item.abilityBonuses.map((bonus) => (
        <span key={bonus.label} className="text-teal">
          +{bonus.value} {bonus.label}
        </span>
      ))}
    </p>
  )
}
