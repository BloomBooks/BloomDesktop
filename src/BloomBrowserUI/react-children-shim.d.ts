// React 17 -> 18 migration bridge.
//
// @types/react 18 removed the implicit `children` prop that function and class
// components used to accept automatically. This codebase was written against
// React 17, where every component implicitly accepted children, and depends on
// that in ~100 places. Instead of annotating each component, we restore the old
// behavior globally by re-adding `children` to the attributes that every JSX
// element accepts. Emotion's JSX namespace (jsxImportSource: "@emotion/react")
// extends React's global JSX.IntrinsicAttributes, so augmenting it here flows
// through to Emotion-flavored elements too.
//
// TECH DEBT: this broadly re-permits `children` on components that might not use
// them, giving up a type-safety improvement React 18 introduced. The plan is to
// unwind it during the React 19 upgrade by deleting this file and declaring
// explicit `children` props on the components that actually take children.
//
// As a head start on that, ~25 components that read props.children internally
// already declare `children?: React.ReactNode` explicitly. Those declarations are
// redundant while this shim is in place, but are the forward-compatible pattern:
// when the shim is deleted, only the components still lacking such a declaration
// will surface as errors, which is exactly the to-do list for finishing the job.

import type * as React from "react";

declare global {
    namespace JSX {
        interface IntrinsicAttributes {
            // ReactNode covers ordinary children. The function form preserves
            // render-prop components (e.g. MUI's Popper, react-transition-group's
            // Transition) whose `children` is a function; a bare ReactNode here would
            // otherwise intersect with their function type and reject it.
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            children?: React.ReactNode | ((...args: any[]) => React.ReactNode);
        }
    }
}

export {};
