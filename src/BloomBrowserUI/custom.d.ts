// Needed to be able to {import} (rather than {require}) SVGs
// See https://webpack.js.org/guides/typescript/#importing-other-assets,
// https://stackoverflow.com/a/45887328,
// https://stackoverflow.com/a/59901802
// Not sure whether we still need it with Vite.
declare module "*.svg" {
    const content: React.FunctionComponent<React.SVGAttributes<SVGElement>>;
    export default content;
}

// Allow importing HTML files as raw strings (for test fixtures)
declare module "*.html?raw" {
    const content: string;
    export default content;
}

// Allow side-effect imports of stylesheets, e.g. import "./App.less".
// TypeScript 6.0 began requiring a module declaration for side-effect imports
// (TS2882); Vite handles the actual loading, so an ambient declaration is enough.
declare module "*.less";
declare module "*.css";

// Bare-specifier side-effect imports that Vite resolves at build time but TypeScript
// cannot. TypeScript 6.0 began erroring on unresolved side-effect imports (TS2882),
// so we declare them ambiently to preserve the prior (silently-allowed) behavior.
declare module "errorHandler";
declare module "jquery.i18n.custom.ts";
declare module "jquery.hasAttr.js";
declare module "long-press/jquery.mousewheel.js";
declare module "long-press/jquery.longpress.js";

declare module "select2/dist/js/select2.js" {
    const select2Factory: (
        root: unknown,
        jQuery: typeof import("jquery"),
    ) => typeof import("jquery");
    export default select2Factory;
}
