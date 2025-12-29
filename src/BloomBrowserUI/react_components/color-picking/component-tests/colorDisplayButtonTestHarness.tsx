import * as React from "react";
import { ColorDisplayButton, DialogResult } from "../colorPickerDialog";
import { BloomPalette } from "../bloomPalette";

export const ColorDisplayButtonTestHarness: React.FunctionComponent = () => {
    return (
        <div>
            <ColorDisplayButton
                disabled={false}
                initialColor="#111111"
                localizedTitle="Background Color"
                transparency={false}
                palette={BloomPalette.CoverBackground}
                width={75}
                onClose={(_result: DialogResult, _newColor: string) => {}}
            />
        </div>
    );
};
