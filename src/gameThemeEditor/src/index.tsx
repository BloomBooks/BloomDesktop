// Public entry point of the self-contained Game Theme Editor project.
// A host mounts the editor into a container element and passes a concrete
// IGameThemeEditorHost; the editor knows nothing else about the host application.

import { createRoot, Root } from "react-dom/client";
import createCache, { EmotionCache } from "@emotion/cache";
import { CacheProvider } from "@emotion/react";
import type { IGameThemeEditorHost } from "./host/IGameThemeEditorHost";
import { GameThemeEditorPanel } from "./GameThemeEditorPanel";

export type { IGameThemeEditorHost, Theme } from "./host/IGameThemeEditorHost";
export { themeVariableNames } from "./themeModel";

// The React root and the Emotion cache are kept together: the cache owns the <style> elements
// Emotion injects into the document head, so unmount() needs it to clean them up again.
interface MountedEditor {
    root: Root;
    cache: EmotionCache;
}
const mountedByContainer = new WeakMap<HTMLElement, MountedEditor>();

/**
 * Mount (or re-render) the editor into the given container, driven by the host.
 *
 * The container may live in a DIFFERENT document than the code calling mount() — in Bloom
 * the host runs in the toolbox iframe but mounts the panel into the editable-page iframe so
 * it can recolor the live page. So we give Emotion a cache anchored to the container's own
 * document head; otherwise the css-prop styles would be injected into the caller's document
 * and never reach the panel.
 */
export function mount(
    container: HTMLElement,
    host: IGameThemeEditorHost,
): void {
    // Reuse the existing root AND cache when re-rendering into the same container. Creating a
    // fresh cache per call would inject a duplicate set of <style> elements every time.
    let mounted = mountedByContainer.get(container);
    if (!mounted) {
        mounted = {
            root: createRoot(container),
            cache: createCache({
                key: "gte",
                container: container.ownerDocument.head,
            }),
        };
        mountedByContainer.set(container, mounted);
    }
    mounted.root.render(
        <CacheProvider value={mounted.cache}>
            <GameThemeEditorPanel host={host} />
        </CacheProvider>,
    );
}

/**
 * Unmount the editor from the container, if present.
 *
 * Emotion injects its <style> elements into the document head rather than into the container,
 * so removing the container (as the host does) does not take them with it. flush() removes the
 * ones this cache inserted; without it, every open/close cycle would leave another set behind.
 */
export function unmount(container: HTMLElement): void {
    const mounted = mountedByContainer.get(container);
    if (mounted) {
        mounted.root.unmount();
        mounted.cache.sheet.flush();
        mountedByContainer.delete(container);
    }
}
