import { defineConfig } from "vite";

// Use dynamic imports so that if Vite/esbuild emits a CommonJS wrapper for this
// config, Node can still load ESM‑only plugins (like @vitejs/plugin-react) via
// native dynamic import instead of require().
export default defineConfig(async () => {
    const [{ default: react }] = await Promise.all([
        import("@vitejs/plugin-react")
        //import("vite-plugin-pug")
    ]);

    return {
        plugins: [
            // Fast Refresh injects $RefreshSig$ which can error in WebView.
            // We disable it here for stability; HMR still works for modules.
            react({ fastRefresh: false })
        ],
        server: {
            port: 5173,
            strictPort: true
        }
    };
});
