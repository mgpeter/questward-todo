import { X } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useId, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

const FOCUSABLE =
  'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),' +
  'textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'

/**
 * Every sheet currently mounted, oldest first.
 *
 * Escape has to close the topmost one only, and the scroll lock has to lift when the last
 * one goes rather than when the first one does. Both are properties of the document rather
 * than of a component, so they live here: two sheets each restoring the body style on their
 * own way out is a page left stuck at position: fixed.
 */
const stack: symbol[] = []
let restoreScrollY = 0

function lockScroll() {
  if (stack.length > 1) return

  restoreScrollY = window.scrollY

  // overflow: hidden on the body is the usual answer and it is not enough on iOS. Safari
  // rubber-bands the document behind the sheet anyway, and a drag that reaches the end of
  // the sheet's own scroller chains straight through to it. Pinning the body takes the
  // document out of the scroll chain; overscroll-contain below stops the chain inside the
  // sheet. Re-applying the offset by hand afterwards is the price of the technique.
  document.body.style.position = 'fixed'
  document.body.style.top = `-${restoreScrollY}px`
  document.body.style.insetInline = '0'
  document.body.style.width = '100%'
}

function unlockScroll() {
  if (stack.length > 0) return

  document.body.style.position = ''
  document.body.style.top = ''
  document.body.style.insetInline = ''
  document.body.style.width = ''
  window.scrollTo(0, restoreScrollY)
}

interface SheetProps {
  open: boolean
  onClose: () => void
  /** Announced as the dialog's name, and drawn in the header. */
  title: string
  /** Sits under the title, and is what aria-describedby points at. */
  description?: ReactNode
  /** Drawn opposite the title, for a live readout like "earns 10 XP". */
  aside?: ReactNode
  children: ReactNode
  /** Pinned below the scroller, and where the safe-area padding lands. */
  footer?: ReactNode
  /** Root test id. The close button derives its own from it. */
  testId?: string
}

/**
 * The bottom sheet every mobile surface is built on.
 *
 * There was no dialog primitive in this codebase before: LevelUpOverlay and ClassSelect each
 * hand-rolled a scrim, and between them managed one Escape handler, no focus trap and no
 * scroll lock. Five more sheets written the same way would be five more chances to forget
 * one of those.
 */
export function Sheet({
  open,
  onClose,
  title,
  description,
  aside,
  children,
  footer,
  testId,
}: SheetProps) {
  const panelRef = useRef<HTMLDivElement>(null)
  const titleId = useId()
  const descriptionId = useId()

  // Held in a ref for the reason LevelUpOverlay holds its dismiss in one: the effect below
  // arms a scroll lock and a focus restore, and both belong to one opening. Keyed on the
  // callback's identity it would tear down and rebuild on every render of the parent.
  const close = useRef(onClose)
  close.current = onClose

  const token = useRef<symbol | null>(null)
  token.current ??= Symbol('sheet')

  useEffect(() => {
    if (!open) return

    const self = token.current!
    const opener = document.activeElement as HTMLElement | null

    stack.push(self)
    lockScroll()

    // The panel, not the first field. Reading the title before the first input is the point
    // of a titled sheet, and autofocusing a text input on a phone throws the keyboard up
    // over the sheet that was just opened.
    panelRef.current?.focus()

    const onKeyDown = (event: KeyboardEvent) => {
      if (stack[stack.length - 1] !== self) return

      if (event.key === 'Escape') {
        // LevelUpOverlay listens for Escape on window too, so stopping here is what makes
        // Escape dismiss one thing at a time, topmost first.
        event.stopPropagation()
        close.current()
        return
      }

      if (event.key !== 'Tab') return

      const panel = panelRef.current
      if (!panel) return

      const stops = [...panel.querySelectorAll<HTMLElement>(FOCUSABLE)].filter(
        (node) => node.offsetParent !== null,
      )
      const first = stops[0] ?? panel
      const last = stops[stops.length - 1] ?? panel
      const active = document.activeElement

      // A wrap rather than `inert` on the app shell, which would be fewer lines and a truer
      // trap. LevelUpOverlay and ContractSettled both render inside that shell and both can
      // fire while a sheet is open; inert would make the celebration unfocusable,
      // unclickable, and invisible to a screen reader.
      if (event.shiftKey && (active === first || active === panel)) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && active === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown, true)

    return () => {
      document.removeEventListener('keydown', onKeyDown, true)
      stack.splice(stack.indexOf(self), 1)
      unlockScroll()

      // Back to whatever opened it. Without this a keyboard reader is returned to the
      // document body and has to tab from the top of the page again.
      opener?.focus?.()
    }
  }, [open])

  // The app shell, not document.body. App's root is `relative z-10`, which is a stacking
  // context: the header at 30 and the level-up overlay at 60 are both painted inside a box
  // whose own z-index is 10. Portalling to the body would make this a sibling of that box,
  // so any z-index above 10 would cover the whole app - including a level-up that fired
  // from inside this very sheet. Escaping to the shell is still enough to leave the card's
  // overflow-hidden and its transformed ancestor behind, which is what the portal is for.
  const host = document.getElementById('app-shell') ?? document.body

  return createPortal(
    <AnimatePresence>
      {open && (
        <div className="fixed inset-0 z-45">
          <motion.div
            aria-hidden="true"
            data-testid="sheet-scrim"
            onClick={() => close.current()}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.22 }}
            className="absolute inset-0"
            // A fixed warm black rather than a themed scrim, for the reason LevelUpOverlay
            // gives: in light mode a pale wash leaves the page behind fully legible and the
            // sheet stops reading as modal at all.
            style={{ backgroundColor: 'rgb(34 31 25 / 0.42)' }}
          />

          <motion.div
            ref={panelRef}
            role="dialog"
            aria-modal="true"
            aria-labelledby={titleId}
            aria-describedby={description ? descriptionId : undefined}
            tabIndex={-1}
            data-testid={testId}
            initial={{ y: '100%', opacity: 0 }}
            animate={{ y: 0, opacity: 1 }}
            exit={{ y: '100%', opacity: 0 }}
            transition={{ duration: 0.22, ease: [0.32, 0.72, 0, 1] }}
            // The cap is measured off the mobile header so the sheet never quite covers the
            // page behind it. dvh rather than vh because Safari's collapsing URL bar makes
            // vh a lie on exactly the devices this is for.
            className="panel absolute inset-x-0 bottom-0 flex max-h-[calc(100dvh-3.5rem)] flex-col rounded-t-2xl outline-none"
          >
            <div className="relative shrink-0 px-4 pt-4 pb-2">
              <span
                aria-hidden="true"
                data-testid="sheet-handle"
                className="absolute top-1.5 left-1/2 h-1 w-9 -translate-x-1/2 rounded-full bg-line-strong/60"
              />

              <div className="flex items-start gap-3">
                <div className="min-w-0 flex-1">
                  <div className="flex items-baseline gap-2.5">
                    <h2
                      id={titleId}
                      className="font-display min-w-0 flex-1 text-[20px] leading-tight"
                    >
                      {title}
                    </h2>
                    {aside}
                  </div>
                  {description && (
                    <p id={descriptionId} className="mt-1 text-[12.5px] text-ink-muted">
                      {description}
                    </p>
                  )}
                </div>

                <button
                  type="button"
                  onClick={() => close.current()}
                  aria-label={`Close ${title}`}
                  data-testid={testId ? `${testId}-close` : 'sheet-close'}
                  className="-mt-1 -mr-1 grid h-11 w-11 shrink-0 place-items-center rounded-lg text-ink-faint transition hover:bg-surface-sunk hover:text-ink"
                >
                  <X size={17} />
                </button>
              </div>
            </div>

            {/* min-h-0 is load-bearing: without it the flex item refuses to shrink below its
                content and the sheet grows past its own max-height instead of scrolling. */}
            <div
              className={`min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 ${
                footer ? 'pb-4' : 'pb-[calc(1rem+env(safe-area-inset-bottom))]'
              }`}
            >
              {children}
            </div>

            {footer && (
              <div className="shrink-0 border-t border-line px-4 pt-3 pb-[calc(0.75rem+env(safe-area-inset-bottom))]">
                {footer}
              </div>
            )}
          </motion.div>
        </div>
      )}
    </AnimatePresence>,
    host,
  )
}
