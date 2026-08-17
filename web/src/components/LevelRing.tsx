import { motion } from 'motion/react'

interface LevelRingProps {
  percent: number
  size?: number
  stroke?: number
  children: React.ReactNode
}

/** Circular XP gauge drawn around the avatar medallion. */
export function LevelRing({ percent, size = 132, stroke = 5, children }: LevelRingProps) {
  const radius = (size - stroke) / 2
  const circumference = 2 * Math.PI * radius
  const clamped = Math.max(0, Math.min(100, percent))

  return (
    <div className="relative" style={{ width: size, height: size }}>
      <svg width={size} height={size} className="-rotate-90" aria-hidden="true">
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke="var(--line)"
          strokeWidth={stroke}
        />
        <motion.circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke="var(--gold)"
          strokeWidth={stroke}
          strokeLinecap="round"
          strokeDasharray={circumference}
          initial={false}
          animate={{ strokeDashoffset: circumference * (1 - clamped / 100) }}
          transition={{ type: 'spring', stiffness: 120, damping: 20 }}
        />
      </svg>

      <div className="absolute inset-0 grid place-items-center">{children}</div>
    </div>
  )
}
