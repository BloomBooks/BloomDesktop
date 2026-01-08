import { css } from "@emotion/react";
import * as React from "react";
import { Typography, MenuItem } from "@mui/material";
import SmallNumberPicker from "../smallNumberPicker";
import { BloomAvatar } from "../bloomAvatar";
import { RadioGroup } from "../RadioGroup";
import WinFormsStyleSelect from "../winFormsStyleSelect";
import BookMakingSettingsControl from "../../collection/bookMakingSettingsControl";
import BloomButton from "../bloomButton";

import { Meta, StoryObj } from "@storybook/react-vite";

const meta: Meta = {
    title: "Misc",
};

export default meta;
type Story = StoryObj;

// Try to simulate the environment of the page preview
const containerDivStyles: React.CSSProperties = {
    width: "500px",
    height: "100%",
    border: "1px solid green",
    flexDirection: "column",
    display: "flex",
    alignItems: "center",
};

const moveToBottomStyles: React.CSSProperties = {
    display: "flex",
    flexDirection: "column",
    justifyContent: "flex-end",
    flex: 1,
    border: "1px solid red",
    width: 200,
};

const previewControlsStyles: React.CSSProperties = {
    display: "flex",
    flexDirection: "row",
    paddingBottom: "20px",
};

const pickerStyles: React.CSSProperties = {
    marginTop: "-10px",
    marginLeft: "-15px",
    position: "absolute",
};

export const SmallNumberPickerStory: Story = {
    name: "SmallNumberPicker",
    render: () => {
        const numberOfPagesTooltip = "Number of pages to add";
        const onHandleChange = (newNumber: number) => {
            console.log("We handled change!");
            console.log(`  result was ${newNumber}`);
        };
        const min = 1;
        const max = 15;

        return (
            <div style={containerDivStyles}>
                <div style={moveToBottomStyles}>
                    <div style={previewControlsStyles}>
                        <BloomButton
                            l10nKey="dummyKey"
                            hasText={true}
                            enabled={false}
                            onClick={() => {
                                console.log("Does nothing");
                            }}
                        >
                            My Button
                        </BloomButton>
                        <div style={pickerStyles}>
                            <SmallNumberPicker
                                minLimit={min}
                                maxLimit={max}
                                handleChange={onHandleChange}
                                tooltip={numberOfPagesTooltip}
                            />
                        </div>
                    </div>
                </div>
            </div>
        );
    },
};

export const BloomAvatarsStory: Story = {
    name: "BloomAvatars",
    render: () => (
        <>
            <BloomAvatar email="test@example.com" name={"A B C D E F G"} />
            <BloomAvatar
                email="test@example.com"
                name={"A B C D E F G"}
                borderColor="green"
            />
            <BloomAvatar
                email="test@example.com"
                name={"A B C D E F G"}
                borderColor="#1d94a4"
            />
            <BloomAvatar email="test@example.com" name={"D E F G"} />
            <BloomAvatar
                email={"andrew" + "_polk" + "@sil.org"}
                name={"A B C D E F G"}
            />
            <BloomAvatar
                email={"andrew" + "_polk" + "@sil.org"}
                name={"A B C"}
                borderColor="#1d94a4"
            />
        </>
    ),
};

export const RadioGroupStory: Story = {
    name: "RadioGroup",
    render: () => (
        <RadioGroup
            choices={{
                short: "Short label",
                long: "Bacon ipsum dolor amet ribeye spare ribs bresaola t-bone. Strip steak turkey shankle pig ground round, biltong t-bone kevin alcatra flank ribeye beef ribs meatloaf filet mignon. Buffalo ham t-bone short ribs. Sausage alcatra tail, sirloin andouille pork belly corned beef shoulder meatloaf venison rump frankfurter bresaola chicken. Ball tip strip steak burgdoggen spare ribs picanha, turducken filet mignon ham hock short loin porchetta rump andouille t-bone boudin.",
            }}
            value={""}
            onChange={() => {}}
        />
    ),
};

const selectItem1 = {
    name: "1st menu item",
    value: "One",
};

const selectItem2 = {
    name: "2nd menu item",
    value: "Two",
};

const selectItem3 = {
    name: "3rd menu item",
    value: "Three",
};

const selectItem4 = {
    name: "4th menu item",
    value: "Four",
};

const selectTestData = [selectItem1, selectItem2, selectItem3, selectItem4];

const selectTestChildren: JSX.Element[] = selectTestData.map((item, index) => {
    return (
        <MenuItem
            key={index}
            value={item.value}
            dense
            css={css`
                padding-top: 0 !important;
                padding-bottom: 0 !important;
            `}
        >
            {item.name}
        </MenuItem>
    );
});

const frameDivStyle: React.CSSProperties = {
    // This is the size of the collection settings dialog tabpanes
    width: "642px",
    height: "452px",
    border: "1px solid green",
    backgroundColor: "#F0F0F0", // winforms control background
};

export const WinFormsImitatingSelectStory: Story = {
    name: "WinForms imitating Select",
    render: () => (
        <div style={frameDivStyle}>
            <WinFormsStyleSelect
                idKey="test1"
                currentValue="Two"
                onChangeHandler={() => {}}
            >
                {selectTestChildren}
            </WinFormsStyleSelect>
        </div>
    ),
};

export const BookMakingTabPaneStory: Story = {
    name: "Book Making tab pane",
    render: () => (
        <div>
            <div style={frameDivStyle}>
                <BookMakingSettingsControl />
            </div>
            <Typography>
                Have Bloom running while testing this and the api calls will
                work.
            </Typography>
        </div>
    ),
};
