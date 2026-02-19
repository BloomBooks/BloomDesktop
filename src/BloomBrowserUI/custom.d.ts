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

declare module "select2/dist/js/select2.js" {
    const select2Factory: (
        root: unknown,
        jQuery: typeof import("jquery"),
    ) => typeof import("jquery");
    export default select2Factory;
}
