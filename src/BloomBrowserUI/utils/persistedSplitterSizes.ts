import { useEffect, useState } from "react";
import { get, postData } from "./bloomApi";

// The sizes of a pair of splitters that divide an area into four quadrants: "widths" for the
// vertical splitter (left pane, right pane) and "heights" for the horizontal one (top, bottom).
// The numbers are relative, not absolute, so they stay meaningful at any window size.
// This deliberately assumes two panes per splitter, which is all we have ever needed.
export type SplitterSizes = {
    widths: number[];
    heights: number[];
};

// The smallest we ever let a pane get. This is also react-collapse-pane's own default, which it
// enforces while dragging but not when applying initialSizes, so we enforce it ourselves when
// restoring a saved layout. See restoreSizes().
export const kMinPaneSizePx = 50;

// What we have read (and since saved) for each setting, so that a component which gets
// unmounted and remounted - as the workspace tabs do - does not lose the user's layout, and
// does not have to wait for the round trip to C# again.
// Undefined for a setting until we have read it; see usePersistedSplitterSizes().
const cachedSizes = new Map<string, SplitterSizes>();

// A pair of sizes we are willing to use. Anything else (an older or hand-edited setting,
// a partly written one) means we fall back to the defaults rather than showing a broken layout.
function isUsablePair(sizes: unknown): sizes is number[] {
    return (
        Array.isArray(sizes) &&
        sizes.length === 2 &&
        sizes.every(
            (size) => typeof size === "number" && isFinite(size) && size >= 0,
        ) &&
        sizes[0] + sizes[1] > 0
    );
}

function parseSavedSizes(
    savedJson: string,
    defaults: SplitterSizes,
): SplitterSizes {
    if (!savedJson) return defaults;
    const saved = JSON.parse(savedJson);
    if (!isUsablePair(saved?.widths) || !isUsablePair(saved?.heights)) {
        return defaults;
    }
    return saved;
}

/**
 * Turn a saved pair into the sizes to open with, keeping neither pane below roughly
 * kMinPaneSizePx. The saved numbers are relative, so a layout chosen in a large window gets
 * scaled down to fit a smaller one; without this, a pane that was merely small when it was
 * saved could come back so thin as to look like it had vanished.
 * Callers pass the window size for availablePx, since the splitter has not been laid out yet
 * when we need this. Where the splitter gets less room than the whole window, the floor works
 * out slightly under kMinPaneSizePx. That is fine: this is a guard against a pane
 * disappearing, not a promise of an exact size. Once the panes are on screen the splitter
 * library enforces kMinPaneSizePx itself while dragging.
 */
export function restoreSizes(
    savedSizes: number[],
    availablePx: number,
): number[] {
    // Half each, if the window is so small that even the minimum doesn't fit twice.
    const minFraction = Math.min(kMinPaneSizePx / availablePx, 0.5);
    const firstFraction = savedSizes[0] / (savedSizes[0] + savedSizes[1]);
    const clamped = Math.min(
        Math.max(firstFraction, minFraction),
        1 - minFraction,
    );
    return [clamped, 1 - clamped];
}

/**
 * Get the sizes saved under this setting name, reading them from the C# user settings the
 * first time we need them. Returns undefined until they are available; after that every
 * remount gets them at once.
 * The setting must exist in Bloom's Settings.settings as a user-scoped string.
 */
export function usePersistedSplitterSizes(
    settingName: string,
    defaults: SplitterSizes,
): SplitterSizes | undefined {
    const [, setHaveSizes] = useState(false);
    // An Effect is right here because reading a setting from C# is talking to an external
    // system, and we want it to happen because the component came on screen. It happens only
    // once per run of Bloom; later mounts are answered from the cache above.
    useEffect(() => {
        if (cachedSizes.has(settingName)) return;
        get(`app/userSetting?settingName=${settingName}`, (result) => {
            cachedSizes.set(
                settingName,
                parseSavedSizes(result.data.settingValue, defaults),
            );
            setHaveSizes(true);
        });
        // We deliberately read the setting once per run, so a later change of defaults (which
        // callers pass as a literal) must not send us back to C#.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [settingName]);
    return cachedSizes.get(settingName);
}

/**
 * Remember the user's new layout, both for the rest of this run and for future ones.
 * Pass only the splitter the user actually dragged; the other one keeps whatever was already
 * saved, which is not necessarily what is on screen (a caller may be overriding it).
 * Only call this once usePersistedSplitterSizes() has produced sizes for this setting.
 */
export function savePersistedSplitterSizes(
    settingName: string,
    draggedSizes: Partial<SplitterSizes>,
) {
    const merged = { ...cachedSizes.get(settingName)!, ...draggedSizes };
    cachedSizes.set(settingName, merged);
    postData("app/userSetting", {
        settingName,
        settingValue: JSON.stringify(merged),
    });
}
