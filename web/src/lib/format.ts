export type DueTone = 'none' | 'future' | 'soon' | 'today' | 'overdue'

export interface DueInfo {
  label: string
  tone: DueTone
}

const startOfDay = (value: Date) =>
  new Date(value.getFullYear(), value.getMonth(), value.getDate())

const dayFormatter = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' })
const fullFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' })

/** Turns a due date into the short phrase and urgency tone shown on the task card. */
export function describeDue(dueDate: string | null): DueInfo {
  if (!dueDate) return { label: '', tone: 'none' }

  const due = new Date(dueDate)
  if (Number.isNaN(due.getTime())) return { label: '', tone: 'none' }

  const days = Math.round(
    (startOfDay(due).getTime() - startOfDay(new Date()).getTime()) / 86_400_000,
  )

  if (days < 0) {
    return {
      label: days === -1 ? 'Yesterday' : `${Math.abs(days)} days overdue`,
      tone: 'overdue',
    }
  }

  if (days === 0) return { label: 'Today', tone: 'today' }
  if (days === 1) return { label: 'Tomorrow', tone: 'soon' }
  if (days <= 6) return { label: `In ${days} days`, tone: 'soon' }

  return { label: dayFormatter.format(due), tone: 'future' }
}

export const formatDate = (value: string | null) =>
  value ? fullFormatter.format(new Date(value)) : ''

/** `2026-08-16` for a date input, in local time rather than UTC. */
export function toDateInputValue(value: string | null): string {
  if (!value) return ''

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''

  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/** A date input value back to an ISO instant at local end of day, so "today" is not already late. */
export function fromDateInputValue(value: string): string | null {
  if (!value) return null

  const [year, month, day] = value.split('-').map(Number)
  if (!year || !month || !day) return null

  return new Date(year, month - 1, day, 23, 59, 59).toISOString()
}
