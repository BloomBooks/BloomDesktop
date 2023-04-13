/* eslint-disable @typescript-eslint/no-empty-function */
// Don't add /** @jsxFrag React.Fragment */ or these stories won't show up in StoryBook! (at least in Aug 2022)
/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { storiesOf } from "@storybook/react";
import { Radio, RadioGroup as MuiRadioGroup, Typography } from "@mui/material";
import { Expandable } from "./expandable";
import { Checkbox } from "./checkbox";
import { BloomCheckbox } from "./BloomCheckBox";
import { useState } from "react";
import { ApiCheckbox } from "./ApiCheckbox";
import BloomButton from "./bloomButton";
import {
    showConfirmDialogFromOutsideReact,
    IConfirmDialogProps,
    ConfirmDialog,
    showConfirmDialog
} from "./confirmDialog";
import ImportIcon from "./icons/ImportIcon";
import DeleteIcon from "@mui/icons-material/Delete";
import PlaybackOrderControls from "./playbackOrderControls";
import SmallNumberPicker from "./smallNumberPicker";
import { BloomAvatar } from "./bloomAvatar";
import { BookInfoCard } from "./bookInfoCard";
import {
    RequiresBloomEnterpriseDialog,
    RequiresBloomEnterpriseNotice,
    RequiresBloomEnterpriseNoticeDialog,
    RequiresBloomEnterpriseOverlayWrapper
} from "./requiresBloomEnterprise";
import { normalDialogEnvironmentForStorybook } from "./BloomDialog/BloomDialogPlumbing";
import {
    LocalizableMenuItem,
    LocalizableCheckboxMenuItem,
    LocalizableNestedMenuItem
} from "./localizableMenuItem";
import {
    Button,
    Divider,
    Menu,
    MenuItem,
    FormControlLabel,
    Checkbox as OriginalMuiCheckbox
} from "@mui/material";
import { RadioGroup } from "./RadioGroup";
import { MuiRadio } from "./muiRadio";
import WinFormsStyleSelect from "./winFormsStyleSelect";
import BookMakingSettingsControl from "../collection/bookMakingSettingsControl";
import { Link } from "./link";
import { BloomSplitButton } from "./bloomSplitButton";
import { AutoUpdateSoftwareDialog } from "./AutoUpdateSoftwareDialog";
import { VisuallyImpairedIcon } from "./icons/VisuallyImpairedIcon";

const kLongText =
    "Bacon ipsum dolor amet ribeye spare ribs bresaola t-bone. Strip steak turkey shankle pig ground round, biltong t-bone kevin alcatra flank ribeye beef ribs meatloaf filet mignon. Buffalo ham t-bone short ribs.";

storiesOf("Localizable Widgets", module)
    .add("Expandable", () => (
        <Expandable
            l10nKey="bogus"
            expandedHeight="30px"
            headingText="I am so advanced"
        >
            Look at this!
        </Expandable>
    ))
    .add("BloomButton", () => (
        <div>
            <BloomButton
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
            >
                Look at this!
            </BloomButton>
            <br /> <br />
            <BloomButton
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
                variant="text"
            >
                Variant = text
            </BloomButton>
            <br /> <br />
            <BloomButton
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
                variant="outlined"
            >
                Variant = outlined
            </BloomButton>
            <br /> <br />
            <BloomButton
                iconBeforeText={<DeleteIcon />}
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
            >
                Material icon
            </BloomButton>
            <br /> <br />
            <BloomButton
                iconBeforeText={<ImportIcon />}
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
                variant="outlined"
            >
                Custom icon
            </BloomButton>
            <br /> <br />
            <BloomButton
                iconBeforeText={<ImportIcon />}
                l10nKey="bogus"
                l10nComment="hello"
                enabled={true}
                hasText={true}
                size="small"
                variant="outlined"
            >
                Small
            </BloomButton>
        </div>
    ))
    .add("BloomSplitButton", () => (
        <div>
            <BloomSplitButton
                options={[
                    {
                        english: "Option 1",
                        l10nId: "already-localized",
                        requiresAnyEnterprise: true,
                        onClick: () => {
                            alert("Option 1 clicked");
                        }
                    },
                    {
                        english: "Option 2",
                        l10nId: "already-localized",
                        onClick: () => {
                            alert("Option 2 clicked");
                        }
                    }
                ]}
            ></BloomSplitButton>
        </div>
    ));

storiesOf("Localizable Widgets/MuiRadio", module).add("MuiRadio", () =>
    React.createElement(() => (
        <MuiRadioGroup>
            our mui radio tweaked for proper alignment when the label wraps:
            <MuiRadio label={"short"} l10nKey={""} onChanged={() => {}} />
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
    ))
);

const useMenuBox = (menuItems: JSX.Element[]) => {
    const [anchorEl, setAnchorEl] = useState<HTMLButtonElement | undefined>(
        undefined
    );
    return (
        <div
            css={css`
                width: 200px;
                height: 100px;
                background-color: tan;
                display: flex;
                flex-direction: row;
                justify-content: center;
                align-items: center;
            `}
        >
            <Button
                color="primary"
                onClick={event =>
                    setAnchorEl(event.target as HTMLButtonElement)
                }
                css={css`
                    background-color: lightblue !important;
                    width: 120px;
                    height: 40px;
                `}
            >
                Click Me!
            </Button>
            <Menu
                anchorEl={anchorEl}
                keepMounted
                open={Boolean(anchorEl)}
                anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
                transformOrigin={{ vertical: "top", horizontal: "left" }}
                onClose={() => {
                    setAnchorEl(undefined);
                }}
            >
                {menuItems}
            </Menu>
        </div>
    );
};

const normalMenuItem = React.createElement(() => (
    <LocalizableMenuItem
        english="Motion Book"
        l10nId="PublishTab.Android.MotionBookMode"
        icon={<DeleteIcon />}
        onClick={() => {}}
        disabled={true}
        tooltipIfDisabled="This has a tooltip!"
    />
));

const checkboxMenuItem = React.createElement(() => (
    <LocalizableCheckboxMenuItem
        english="Decodable Reader"
        l10nId="TemplateBooks.BookName.Decodable Reader"
        apiEndpoint="some/api/endpoint"
        onClick={() => {}}
    />
));

const normalMenuItemWithEllipsisAndEnterprise = React.createElement(() => (
    <LocalizableMenuItem
        english="Open or Create Another Collection"
        l10nId="CollectionTab.OpenCreateCollectionMenuItem"
        addEllipsis={true}
        requiresAnyEnterprise={true}
        onClick={() => {}}
    />
));

const requiresEnterpriseSubscriptionWithIcon = React.createElement(() => (
    <LocalizableMenuItem
        english="BE subscription required, has disabled icon"
        l10nId="already-localized"
        requiresEnterpriseSubscription={true}
        icon={<DeleteIcon />}
        onClick={() => {}}
    />
));

const nestedMenu = React.createElement(() => (
    <LocalizableNestedMenuItem
        english="Troubleshooting"
        l10nId="CollectionTab.ContextMenu.Troubleshooting"
    >
        {[
            normalMenuItem,
            checkboxMenuItem,
            normalMenuItemWithEllipsisAndEnterprise,
            requiresEnterpriseSubscriptionWithIcon
        ]}
    </LocalizableNestedMenuItem>
));

const divider = React.createElement(() => <Divider />);

const testMenu = [
    normalMenuItem,
    normalMenuItemWithEllipsisAndEnterprise,
    requiresEnterpriseSubscriptionWithIcon,
    checkboxMenuItem,
    divider,
    nestedMenu
];

storiesOf("Localizable Widgets/Localizable Menu", module).add("test menu", () =>
    useMenuBox(testMenu)
);

storiesOf("Localizable Widgets/Link", module).add("enabled", () => (
    <Link l10nKey="bogus">link text</Link>
));

// Setting the disabled prop actually only adds a disabled class which has no effect on its own.
// So I'm not including the story for now. Else it is just confusing.
// .add("disabled", () => (
//     <Link l10nKey="bogus" disabled={true}>
//         disabled link text
//     </Link>
// ))

const confirmDialogProps: IConfirmDialogProps = {
    title: "Title",
    titleL10nKey: "",
    message: "Message",
    messageL10nKey: "",
    confirmButtonLabel: "OK",
    confirmButtonLabelL10nKey: "",
    onDialogClose: dialogResult => {
        alert(dialogResult);
    }
};

// Try to simulate the environment of the page preview
const containerDivStyles: React.CSSProperties = {
    width: "500px",
    height: "100%",
    border: "1px solid green",
    flexDirection: "column",
    display: "flex",
    alignItems: "center"
};

const moveToBottomStyles: React.CSSProperties = {
    display: "flex",
    flexDirection: "column",
    justifyContent: "flex-end",
    flex: 1,
    border: "1px solid red",
    width: 200
};

const previewControlsStyles: React.CSSProperties = {
    display: "flex",
    flexDirection: "row",
    paddingBottom: "20px"
};

const pickerStyles: React.CSSProperties = {
    marginTop: "-10px",
    marginLeft: "-15px",
    position: "absolute"
};

storiesOf("Misc", module)
    .add("ConfirmDialog", () =>
        React.createElement(() => (
            <div>
                <div id="modal-container" />
                <BloomButton
                    onClick={() => showConfirmDialog()}
                    enabled={true}
                    hasText={true}
                    l10nKey={"dummyKey"}
                >
                    Open Confirm Dialog
                </BloomButton>
                <ConfirmDialog {...confirmDialogProps} />
            </div>
        ))
    )
    .add("ConfirmDialog as launched from outside React", () =>
        React.createElement(() => (
            <div>
                <div id="modal-container" />
                <BloomButton
                    onClick={() =>
                        showConfirmDialogFromOutsideReact(
                            confirmDialogProps,
                            document.getElementById("modal-container")
                        )
                    }
                    enabled={true}
                    hasText={true}
                    l10nKey={"dummyKey"}
                >
                    Open Confirm Dialog
                </BloomButton>
            </div>
        ))
    )
    .add("Small Number Picker", () =>
        React.createElement(() => {
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
        })
    )
    .add("BloomAvatars", () =>
        React.createElement(() => {
            return (
                <React.Fragment>
                    <BloomAvatar
                        email="test@example.com"
                        name={"A B C D E F G"}
                    />
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
                </React.Fragment>
            );
        })
    )
    .add("AutoUpdateSoftwareDialog", () =>
        React.createElement(() => (
            <AutoUpdateSoftwareDialog
                dialogEnvironment={normalDialogEnvironmentForStorybook}
            />
        ))
    )
    .add("RadioGroup", () =>
        React.createElement(() => {
            return (
                <RadioGroup
                    choices={{
                        short: "Short label",
                        long:
                            "Bacon ipsum dolor amet ribeye spare ribs bresaola t-bone. Strip steak turkey shankle pig ground round, biltong t-bone kevin alcatra flank ribeye beef ribs meatloaf filet mignon. Buffalo ham t-bone short ribs. Sausage alcatra tail, sirloin andouille pork belly corned beef shoulder meatloaf venison rump frankfurter bresaola chicken. Ball tip strip steak burgdoggen spare ribs picanha, turducken filet mignon ham hock short loin porchetta rump andouille t-bone boudin."
                    }}
                    value={""}
                    onChange={() => {}}
                />
            );
        })
    );

const selectItem1 = {
    name: "1st menu item",
    value: "One"
};

const selectItem2 = {
    name: "2nd menu item",
    value: "Two"
};

const selectItem3 = {
    name: "3rd menu item",
    value: "Three"
};

const selectItem4 = {
    name: "4th menu item",
    value: "Four"
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
    backgroundColor: "#F0F0F0" // winforms control background
};

storiesOf("Misc/Collection Settings", module)
    .add("WinForms imitating Select", () =>
        React.createElement(() => (
            <div style={frameDivStyle}>
                <WinFormsStyleSelect
                    idKey="test1"
                    currentValue="Two"
                    onChangeHandler={() => {}}
                >
                    {selectTestChildren}
                </WinFormsStyleSelect>
            </div>
        ))
    )
    .add("Book Making tab pane", () =>
        React.createElement(() => (
            <div>
                <div style={frameDivStyle}>
                    <BookMakingSettingsControl />
                </div>
                <Typography>
                    Have Bloom running while testing this and the api calls will
                    work.
                </Typography>
            </div>
        ))
    );

const playbackControlsDivStyles: React.CSSProperties = {
    width: "150px",
    height: "80px",
    border: "1px solid red",
    display: "flex",
    justifyContent: "center"
};

const bumpUp = (whichPositionToBump: number): void => {
    console.log(
        `Bump up myOrderNum from ${whichPositionToBump} to ${++whichPositionToBump}`
    );
};

const bumpDown = (whichPositionToBump: number): void => {
    console.log(
        `Bump down myOrderNum from ${whichPositionToBump} to ${--whichPositionToBump}`
    );
};

storiesOf("PlaybackOrderControls", module).add("PlaybackOrder buttons", () =>
    React.createElement(() => (
        <React.Fragment>
            <div style={playbackControlsDivStyles}>
                <PlaybackOrderControls
                    maxOrder={3}
                    orderOneBased={2}
                    onIncrease={bumpUp}
                    onDecrease={bumpDown}
                />
            </div>
            <div style={playbackControlsDivStyles}>
                <PlaybackOrderControls
                    maxOrder={3}
                    orderOneBased={1}
                    onIncrease={bumpUp}
                    onDecrease={bumpDown}
                />
            </div>
            <div style={playbackControlsDivStyles}>
                <PlaybackOrderControls
                    maxOrder={3}
                    orderOneBased={3}
                    onIncrease={bumpUp}
                    onDecrease={bumpDown}
                />
            </div>
        </React.Fragment>
    ))
);

const languages1: string[] = ["Kanuri", "Swahili"];
const languages2: string[] = ["French", "Swahili"];
const languages3: string[] = [
    "French",
    "Swahili",
    "Tanzanian Sign Language",
    "English"
];
const uploadDate = "7/28/2020";
const updateDate = "10/26/2020";
storiesOf("BookInformationCards", module)
    .add("Previously Uploaded", () =>
        React.createElement(() => (
            <BookInfoCard
                title="02. Bigǝ Dinaro Gaana"
                languages={languages1}
                originalUpload={uploadDate}
                lastUpdated={updateDate}
            />
        ))
    )
    .add("New Upload", () =>
        React.createElement(() => (
            <BookInfoCard title="02. Foo Bar" languages={languages2} />
        ))
    )
    .add("Several languages", () =>
        React.createElement(() => (
            <BookInfoCard title="Foo Bar Extended" languages={languages3} />
        ))
    );

// These components perform api calls. You'll need Bloom running
// with a collection which doesn't have enterprise enabled if you
// want things to show up as expected.
storiesOf("RequiresBloomEnterprise", module)
    .add("RequiresBloomEnterpriseNoticeDialog", () =>
        React.createElement(() => <RequiresBloomEnterpriseNoticeDialog />)
    )
    .add("RequiresBloomEnterpriseDialog", () =>
        React.createElement(() => (
            <RequiresBloomEnterpriseDialog
                dialogEnvironment={normalDialogEnvironmentForStorybook}
            />
        ))
    )
    .add("RequiresBloomEnterpriseNotice", () =>
        React.createElement(() => <RequiresBloomEnterpriseNotice />)
    )
    .add("RequiresBloomEnterpriseOverlayWrapper", () =>
        React.createElement(() => <RequiresBloomEnterpriseOverlayWrapper />)
    );
