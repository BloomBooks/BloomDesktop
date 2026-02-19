// Wrapper to export xregexp for browser mode
// Import from our local copy of the UMD bundle
// Some AI wrote this as part of our efforts to get non-module code to import properly for Vite/esnext.
// As the comment above indicates, it MAY only be necessary when we attempt to use
// vitest in browser mode...or (since we haven't yet gotten that working)
// it MAY not be useful at all.
import XRegExp from "./xregexp-all.js";

export default XRegExp;
