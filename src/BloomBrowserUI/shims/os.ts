// Browser shim for Node's 'os' module used by some dependencies (e.g., contentful-sdk-core).
// We only need to provide the named exports that may be imported: platform and release.
// In the browser build, these are never actually used by the library code paths we execute,
// but bundlers need the symbols to exist to avoid import errors.

export const platform = (): string => ""; // e.g., 'win32', 'linux' in Node; empty for browser
export const release = (): string => ""; // e.g., OS version in Node; empty for browser
