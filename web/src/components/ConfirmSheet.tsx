import { useEffect, useState, type ReactNode } from 'react'
import { Sheet } from './Sheet'

interface ConfirmSheetProps {
  open: boolean
  onClose: () => void
  onConfirm: () => void
  title: string
  description?: string
  /** What is about to happen, in the caller's own words. Usually two lists. */
  children?: ReactNode
  confirmLabel: string
  /** Shown while the request is in flight, and the button is disabled with it. */
  pending?: boolean
  /**
   * A word the player must type before the button arms.
   *
   * Reserved for the actions with nothing behind them. Everything else in this app commits on
   * one click, and adding a step to a reversible action would only teach people to click
   * through the step.
   */
  typeToConfirm?: string
  testId?: string
}

/**
 * The first confirmation in this codebase, and deliberately not the last word on every
 * destructive action.
 *
 * Deleting a task, clearing the finished column and salvaging an item all still commit on one
 * click, and should: each is one row, and the shop already states the house position that the
 * price belongs on the button rather than behind a dialog. This exists for the two actions that
 * delete an era or an account, where there is no undo and no way to re-earn what went.
 */
export function ConfirmSheet({
  open,
  onClose,
  onConfirm,
  title,
  description,
  children,
  confirmLabel,
  pending = false,
  typeToConfirm,
  testId = 'confirm-sheet',
}: ConfirmSheetProps) {
  const [typed, setTyped] = useState('')

  // Cleared on every opening rather than on close, so a sheet dismissed mid-type does not come
  // back already armed.
  useEffect(() => {
    if (open) setTyped('')
  }, [open])

  const armed = typeToConfirm === undefined || typed.trim() === typeToConfirm

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title={title}
      description={description}
      testId={testId}
      footer={
        <div className="flex gap-2.5">
          <button
            type="button"
            onClick={onClose}
            className="flex-1 rounded-xl border border-line py-2.5 text-[14px] transition hover:bg-surface-sunk"
          >
            Cancel
          </button>

          <button
            type="button"
            onClick={onConfirm}
            disabled={!armed || pending}
            data-testid={`${testId}-confirm`}
            className="flex-1 rounded-xl border border-rose/40 bg-rose/10 py-2.5 text-[14px] text-rose transition hover:bg-rose/20 disabled:opacity-40"
          >
            {pending ? 'Working...' : confirmLabel}
          </button>
        </div>
      }
    >
      <div className="space-y-4 pt-1 text-[13.5px]">
        {children}

        {typeToConfirm !== undefined && (
          <label className="block">
            <span className="block text-[12px] text-ink-muted">
              Type <span className="text-ink">{typeToConfirm}</span> to confirm.
            </span>

            <input
              value={typed}
              onChange={(event) => setTyped(event.target.value)}
              autoComplete="off"
              spellCheck={false}
              data-testid={`${testId}-input`}
              className="mt-1.5 w-full rounded-xl border border-line bg-surface-sunk px-3 py-2 text-[14px] outline-none focus:border-rose/50"
            />
          </label>
        )}
      </div>
    </Sheet>
  )
}
