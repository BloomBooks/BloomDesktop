import * as React from "react";
import { useState } from "react";
import { ColorDisplayButton, DialogResult } from "../colorPickerDialog";
import { BloomPalette } from "../bloomPalette";

export const ColorDisplayButtonTestHarness: React.FunctionComponent<{
    initialColor?: string;
    deferOnChangeUntilComplete?: boolean;
}> = (props) => {
    const [changeCount, setChangeCount] = useState(0);
    const [lastChangedColor, setLastChangedColor] = useState("");
    const [closeResult, setCloseResult] = useState("");

    return (
        <div>
            <div data-testid="change-count">{changeCount}</div>
            <div data-testid="last-changed-color">{lastChangedColor}</div>
            <div data-testid="close-result">{closeResult}</div>
            <ColorDisplayButton
                disabled={false}
                initialColor={props.initialColor ?? "#111111"}
                localizedTitle="Background Color"
                transparency={false}
                palette={BloomPalette.CoverBackground}
                width={75}
                deferOnChangeUntilComplete={
                    props.deferOnChangeUntilComplete ?? false
                }
                onChange={(newColor: string) => {
                    setChangeCount((previousCount) => previousCount + 1);
                    setLastChangedColor(newColor);
                }}
                onClose={(result: DialogResult, _newColor: string) => {
                    setCloseResult(
                        result === DialogResult.OK ? "ok" : "cancel",
                    );
                }}
            />
        </div>
    );
};
