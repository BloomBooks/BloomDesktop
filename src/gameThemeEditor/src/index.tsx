// Public entry point of the self-contained Game Theme Editor project.
// A host mounts the editor into a container element and passes a concrete
// IGameThemeEditorHost; the editor knows nothing else about the host application.

import { createRoot, Root } from "react-dom/client";
import createCache from "@emotion/cache";
import { CacheProvider } from "@emotion/react";
import type { IGameThemeEditorHost } from "./host/IGameThemeEditorHost";
import { GameThemeEditorPanel } from "./GameThemeEditorPanel";

export type { IGameThemeEditorHost, Theme } from "./host/IGameThemeEditorHost";
export { themeVariableNames } from "./themeModel";

const rootsByContainer = new WeakMap<HTMLElement, Root>();

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
    let root = rootsByContainer.get(container);
    if (!root) {
        root = createRoot(container);
        rootsByContainer.set(container, root);
    }
    const cache = createCache({
        key: "gte",
        container: container.ownerDocument.head,
    });
    root.render(
        <CacheProvider value={cache}>
            <GameThemeEditorPanel host={host} />
        </CacheProvider>,
    );
}

/** Unmount the editor from the container, if present. */
export function unmount(container: HTMLElement): void {
    const root = rootsByContainer.get(container);
    if (root) {
        root.unmount();
        rootsByContainer.delete(container);
    }
}
