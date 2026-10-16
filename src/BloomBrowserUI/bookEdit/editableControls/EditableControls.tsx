import { css } from "@emotion/react";
import * as React from "react";
import { useState, useEffect } from "react";
import { ThemeProvider } from "@mui/material/styles";
import { default as KeyboardIcon } from "@mui/icons-material/Keyboard";
import { lightTheme } from "../../bloomMaterialUITheme";
import { useL10n } from "../../react_components/l10nHooks";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import {
    FieldKeyboardInfo,
    getKeyboardInfoFor,
    kFieldKeyboardInfoEvent,
    showOsk,
} from "../js/keymanWebIntegration";

// Look up the display name for a language code, matching the old jQuery
// AddLanguageTags logic: use the localized name, falling back to the raw code
// if we have no localization for it. Exported so the canvas-element context
// controls can compute the same name without depending on the now-removed
// data-languageTipContent attribute.
export function getLanguageDisplayName(langCode: string | null): string {
    if (!langCode) return "";
    return theOneLocalizationManager.getLanguageName(langCode) || langCode;
}

// A small strip of React-hosted controls that is attached (as an out-of-flow
// sibling, never a child) to a single .bloom-editable. It currently hosts the
// language tag (formerly a CSS ::after pseudo-element) and a keyboard indicator
// that re-shows the KeymanWeb on-screen keyboard. See editableControlsManager.ts
// for the non-React lifecycle/positioning owner.
//
// props.editable  the bloom-editable this strip adorns
// props.focused    whether that editable currently has focus (drives the
//                  keyboard indicator, which is only relevant for the field the
//                  user is actually typing in)
// props.showTag    whether the language tag should be shown (the manager decides
//                  this from the same rules the old AddLanguageTags used)
export const EditableControls: React.FunctionComponent<{
    editable: HTMLElement;
    focused: boolean;
    showTag: boolean;
}> = (props) => {
    const tagText = getLanguageDisplayName(props.editable.getAttribute("lang"));

    // The keyboard info arrives asynchronously: the field's focusin handler
    // POSTs keyboarding/fieldFocused and only then knows whether this field uses
    // a KeymanWeb keyboard. This strip is mounted before that (at page setup),
    // so we seed from whatever is already cached and then listen for the
    // notification dispatched on the editable when a (possibly later) response
    // comes back.
    const [keyboardInfo, setKeyboardInfo] = useState<
        FieldKeyboardInfo | undefined
    >(() => getKeyboardInfoFor(props.editable));

    useEffect(() => {
        const handler = (e: Event) => {
            setKeyboardInfo((e as CustomEvent<FieldKeyboardInfo>).detail);
        };
        props.editable.addEventListener(kFieldKeyboardInfoEvent, handler);
        // Pick up anything that was resolved between our initial render and this
        // effect running.
        setKeyboardInfo(getKeyboardInfoFor(props.editable));
        return () => {
            props.editable.removeEventListener(
                kFieldKeyboardInfoEvent,
                handler,
            );
        };
    }, [props.editable]);

    const showKeyboardIndicator = props.focused && !!keyboardInfo?.useKmw;

    const showOskTooltip = useL10n(
        "Show on-screen keyboard",
        "EditTab.EditableControls.ShowOnScreenKeyboard",
    );

    return (
        <ThemeProvider theme={lightTheme}>
            <div
                // A right-aligned horizontal strip pinned to the bottom of the
                // editable. pointer-events are off so clicks fall through to the
                // text; only the interactive keyboard button turns them back on.
                css={css`
                    display: flex;
                    flex-direction: row;
                    align-items: flex-end;
                    gap: 4px;
                    pointer-events: none;
                    line-height: 1;
                `}
            >
                {showKeyboardIndicator && (
                    <button
                        type="button"
                        title={showOskTooltip}
                        aria-label={showOskTooltip}
                        // Keep focus in the editable so the OSK stays tied to the
                        // field (and so this indicator, which is only shown while
                        // the field is focused, does not vanish out from under the
                        // click).
                        onMouseDown={(e) => e.preventDefault()}
                        onClick={() => showOsk()}
                        css={css`
                            pointer-events: all;
                            cursor: pointer;
                            padding: 0;
                            margin: 0;
                            border: none;
                            background: none;
                            display: flex;
                            align-items: center;
                            color: var(--language-tag-color);
                            &:hover {
                                color: black;
                            }
                        `}
                    >
                        <KeyboardIcon
                            css={css`
                                font-size: 16px;
                            `}
                        />
                    </button>
                )}
                {props.showTag && tagText && (
                    <span
                        // Match the typography of the old CSS language tip.
                        // (Hover-gating is handled by the container's visibility
                        // in editMode.less, not per-control.)
                        css={css`
                            color: var(--language-tag-color);
                            font-family: Roboto, NotoSans, sans-serif;
                            font-size: small;
                            font-style: normal;
                            font-weight: normal;
                            text-shadow: none;
                            white-space: nowrap;
                        `}
                    >
                        {tagText}
                    </span>
                )}
            </div>
        </ThemeProvider>
    );
};
