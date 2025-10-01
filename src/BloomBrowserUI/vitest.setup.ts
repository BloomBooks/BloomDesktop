import "@testing-library/jest-dom/vitest";
// import { vi } from "vitest";
// vi.stubGlobal(
//     "ResizeObserver",
//     vi.fn(() => ({
//         disconnect: vi.fn(),
//         observe: vi.fn(),
//         unobserve: vi.fn()
//     }))
// );
// import "vi-canvas-mock";
import jQuery from "jquery";
globalThis.$ = jQuery;
globalThis.jQuery = jQuery;
