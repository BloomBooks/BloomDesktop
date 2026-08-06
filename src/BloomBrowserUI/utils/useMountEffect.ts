import { useEffect } from "react";

/**
 * Runs the given effect exactly once, when the component mounts, and runs its
 * returned cleanup (if any) when the component unmounts.
 *
 * This is the preferred way to express a "run on mount" effect. A bare
 * `useEffect(effect, [])` makes the eslint react-hooks/exhaustive-deps rule
 * complain about the empty dependency array, which tempts people to either
 * add dependencies they don't want or scatter eslint-disable comments through
 * the codebase. Funneling the pattern through this helper states the intent
 * clearly ("this runs on mount") and contains the single, justified
 * exhaustive-deps suppression in one place.
 *
 * Note that mount effects are still effects: before reaching for one, see
 * .github/skills/react-useeffect to confirm an effect is actually warranted
 * rather than (for example) deriving a value during render or handling
 * something in an event handler.
 */
export function useMountEffect(effect: () => void | (() => void)) {
    // eslint-disable-next-line react-hooks/exhaustive-deps
    useEffect(effect, []);
}
