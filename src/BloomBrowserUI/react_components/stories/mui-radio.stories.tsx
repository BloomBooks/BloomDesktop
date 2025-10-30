import { css } from "@emotion/react";
import {
    Radio,
    RadioGroup as MuiRadioGroup,
    FormControlLabel,
} from "@mui/material";
import { MuiRadio } from "../muiRadio";

import { Meta, StoryObj } from "@storybook/react-vite";

const meta: Meta = {
    title: "Localizable Widgets/MuiRadio",
};

export default meta;
type Story = StoryObj;

export const MuiRadioStory: Story = {
    name: "MuiRadio",
    render: () => (
        <MuiRadioGroup>
            our mui radio tweaked for proper alignment when the label wraps:
            <MuiRadio label={"short"} l10nKey={""} />
            <MuiRadio
                label={
                    "Bacon ipsum dolor amet ribeye spare ribs bresaola t-bone. Strip steak turkey shankle pig ground round, biltong t-bone kevin alcatra flank ribeye beef ribs meatloaf filet mignon. Buffalo ham t-bone short ribs. Sausage alcatra tail, sirloin andouille pork belly corned beef shoulder meatloaf venison rump frankfurter bresaola chicken. Ball tip strip steak burgdoggen spare ribs picanha, turducken filet mignon ham hock short loin porchetta rump andouille t-bone boudin."
                }
                l10nKey={""}
            />
            <hr />
            original mui radio:
            <FormControlLabel control={<Radio />} label={"short"} />
            <FormControlLabel
                control={<Radio />}
                label={
                    "Bacon ipsum dolor amet ribeye spare ribs bresaola t-bone. Strip steak turkey shankle pig ground round, biltong t-bone kevin alcatra flank ribeye beef ribs meatloaf filet mignon. Buffalo ham t-bone short ribs. Sausage alcatra tail, sirloin andouille pork belly corned beef shoulder meatloaf venison rump frankfurter bresaola chicken. Ball tip strip steak burgdoggen spare ribs picanha, turducken filet mignon ham hock short loin porchetta rump andouille t-bone boudin."
                }
            />
            <hr />
            ours followed by original:
            <div
                css={css`
                    display: flex;
                `}
            >
                <MuiRadio label={"short"} l10nKey={""} />
                <FormControlLabel control={<Radio />} label={"short"} />
            </div>
        </MuiRadioGroup>
    ),
};
