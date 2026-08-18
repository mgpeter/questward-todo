import { useEffect, useRef } from 'react'
import { prefersReducedMotion } from './sound'

/**
 * Keeps a scrolling container pinned to its newest content.
 *
 * The combat log has always had `overflow-y-auto`, so it scrolled; what it never did was
 * move. Once a fight passed the container's height the newest round landed below the fold
 * and the player had to scroll to find out what had just happened to them, which reads as
 * the log having stopped.
 *
 * Deliberately not unconditional. Someone who has scrolled up is reading, and yanking them
 * back to the bottom on the next round is worse than the original complaint, so the pin
 * releases the moment they leave the bottom and re-engages when they return.
 *
 * @param dependency Something that changes when content is appended, such as the entry count.
 */
export function useStickToBottom<T extends HTMLElement>(dependency: unknown) {
  const ref = useRef<T>(null)

  // Whether the reader is at the bottom, sampled BEFORE the new content lands. Read after
  // the paint and it is always false, because the fresh entry has already pushed the
  // viewport up. A ref rather than state: this must not itself cause a render.
  const pinned = useRef(true)

  useEffect(() => {
    const element = ref.current
    if (!element) return

    if (pinned.current) {
      element.scrollTo({
        top: element.scrollHeight,
        // Smooth scrolling is motion, and someone who has asked for less of it gets the
        // jump instead. They still end up in the right place.
        behavior: prefersReducedMotion() ? 'auto' : 'smooth',
      })
    }
  }, [dependency])

  const onScroll = () => {
    const element = ref.current
    if (!element) return

    // A tolerance, because a smooth scroll settles a fraction of a pixel short and an exact
    // comparison would unpin the reader every time it finished.
    const distance = element.scrollHeight - element.scrollTop - element.clientHeight
    pinned.current = distance < 24
  }

  return { ref, onScroll }
}
