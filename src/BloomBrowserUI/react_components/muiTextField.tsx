/** @jsx jsx **/
import { jsx } from "@emotion/react";

import * as React from "react";
import { TextField, TextFieldProps } from "@mui/material";

import { useL10n } from "./l10nHooks";
import { ILocalizationProps } from "./l10nComponents";

// Static map to track input elements and their associated onChange handlers
export const inputToOnChangeMap = new Map<
    HTMLInputElement,
    React.ChangeEventHandler<HTMLInputElement>
>();

// All this complexity works around the difficulty of using code to insert text into a controlled <input>.
// The problem started when we wanted pasting an image to create a canvas element. That meant we needed to
// take control of Paste as a whole, rather than just letting the browser handle it. We ended up with a
// function responsible for inserting the (pasted) text at the appropriate place. Most possible places
// are handled by ckeditor or some simple code, but pasting into a controlled <input> not controlled by
// ckeditor is hard. The MuiTextField tries very hard to ignore what we do to the input. If the user goes
// ahead and types something, a simple insert will work, but if focus changes first, the new value gets
// forgotten, unless we manage to simulate the event at the React level. For reasons I dont understand,
// it's not enough to dispatch an InputEvent at the <input> level; we have to actually call the onChange
// handler that was passed in to the MuiTextField. Unfortunately, the paste code has no way to get at
// the React component, so this wrapper builds a map of active <input>s and their onChange handlers.
// That allows this function to call the right one. Unmounting cleans up. I'm not sure we need to simulate
// the event object as fully as we do, but it seemed more future-proof.
// The expectation is that the <input> was found using document.activeElement and therefore contains
// the selection.
export function insertIntoInputAtSelection(
    input: HTMLInputElement,
    text: string
): void {
    const start = input.selectionStart || 0;
    const end = input.selectionEnd || 0;

    // Replace the selected text with the pasted text
    const currentValue = input.value;
    const newValue =
        currentValue.substring(0, start) + text + currentValue.substring(end);
    input.value = newValue;

    // Move cursor to the end of the pasted text
    input.selectionStart = input.selectionEnd = start + text.length;
    // Dispatch events as nearly as possible emulating what would happen if the user had
    // pasted the text directly into the input.
    const inputEvent = new InputEvent("input", {
        bubbles: true,
        cancelable: true,
        composed: true,
        data: text,
        inputType: "insertText"
    });
    // If the input is a controlled React component we know about, we call its onChange handler.
    // (I can't find any event to dispatch at the <input> level that causes this to get raised.)
    const changeFn = inputToOnChangeMap.get(input);
    if (changeFn) {
        // Create a more complete synthetic React change event
        const syntheticEvent = {
            target: input,
            currentTarget: input,
            nativeEvent: inputEvent,
            bubbles: true,
            cancelable: true,
            defaultPrevented: false,
            eventPhase: 0,
            isTrusted: true,
            preventDefault: () => {},
            isDefaultPrevented: () => false,
            stopPropagation: () => {},
            isPropagationStopped: () => false,
            persist: () => {},
            timeStamp: Date.now(),
            type: "change",
            // Add value explicitly as MUI might look for it
            value: input.value
        };

        changeFn(
            (syntheticEvent as unknown) as React.ChangeEvent<HTMLInputElement>
        );
    } else {
        // Not one we know about. It might be uncontrolled, or not involve React at all.
        // This MIGHT help; I haven't tested it. (Note that dispatchEvent sets the target.)
        input.dispatchEvent(inputEvent);

        const changeEvent = new Event("change", { bubbles: true });
        input.dispatchEvent(changeEvent);
    }
}

// wrap up the material-ui text field in something localizable
export const MuiTextField: React.FunctionComponent<ILocalizationProps &
    TextFieldProps & {
        label: string;
    }> = props => {
    const localizedLabel = useL10n(
        props.label,
        props.alreadyLocalized || props.temporarilyDisableI18nWarning
            ? null
            : props.l10nKey,
        props.l10nComment,
        props.l10nParam0,
        props.l10nParam1
    );

    const { label, value, onChange, ...propsToPass } = props;

    // Create a ref to access the input element directly
    const inputRef = React.useRef<HTMLInputElement>(null);

    // Run once when the component mounts, then check if the inputRef has a value
    React.useEffect(() => {
        // We need to access the current value inside the effect, not in the dependency array
        const input = inputRef.current;
        if (input && onChange) {
            // Add the input element and its onChange handler to the map
            inputToOnChangeMap.set(input, onChange);

            // Clean up when component unmounts
            return () => {
                inputToOnChangeMap.delete(input);
            };
        }
        return undefined;
    }, [onChange]);

    return (
        <TextField
            label={localizedLabel}
            value={value}
            onChange={onChange}
            variant="outlined"
            InputLabelProps={{
                shrink: true
            }}
            inputRef={inputRef}
            {...propsToPass}
        />
    );
};
