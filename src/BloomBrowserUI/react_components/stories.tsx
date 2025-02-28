/* eslint-disable @typescript-eslint/no-empty-function */
// Don't add /** @jsxFrag React.Fragment */ or these stories won't show up in StoryBook! (at least in Aug 2022)
/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
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
import { ImportIcon } from "../bookEdit/toolbox/talkingBook/TalkingBookToolboxIcons";
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
import {
    StorybookDialogWrapper,
    normalDialogEnvironmentForStorybook
} from "./BloomDialog/BloomDialogPlumbing";
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
import { ForumInvitationDialogLauncher } from "./forumInvitationDialog";

const kLongText =
    "Bacon ipsum dolor amet ribeye spare ribs bresaola t-bone. Strip steak turkey shankle pig ground round, biltong t-bone kevin alcatra flank ribeye beef ribs meatloaf filet mignon. Buffalo ham t-bone short ribs.";

export default {
    title: "Localizable Widgets"
};

export const _Expandable = () => (
    <Expandable
        l10nKey="bogus"
        expandedHeight="30px"
        headingText="I am so advanced"
    >
        Look at this!
    </Expandable>
);

export const _BloomButton = () => (
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
);

_BloomButton.story = {
    name: "BloomButton"
};

export const _BloomSplitButton = () => (
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
);

_BloomSplitButton.story = {
    name: "BloomSplitButton"
};

export default {
    title: "Localizable Widgets/MuiRadio"
};

export const _MuiRadio = () =>
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
    ));

_MuiRadio.story = {
    name: "MuiRadio"
};

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

export default {
    title: "Localizable Widgets/Localizable Menu"
};

export const TestMenu = () => useMenuBox(testMenu);

TestMenu.story = {
    name: "test menu"
};

export default {
    title: "Localizable Widgets/Link"
};

export const Enabled = () => <Link l10nKey="bogus">link text</Link>;

Enabled.story = {
    name: "enabled"
};

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

export default {
    title: "Misc/Dialogs"
};

export const _ConfirmDialog = () =>
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
    ));

_ConfirmDialog.story = {
    name: "ConfirmDialog"
};

export const ConfirmDialogAsLaunchedFromOutsideReact = () =>
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
    ));

ConfirmDialogAsLaunchedFromOutsideReact.story = {
    name: "ConfirmDialog as launched from outside React"
};

export const _AutoUpdateSoftwareDialog = () =>
    React.createElement(() => (
        <AutoUpdateSoftwareDialog
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    ));

_AutoUpdateSoftwareDialog.story = {
    name: "AutoUpdateSoftwareDialog"
};

export const ForumInvitationDialog = () => (
    <StorybookDialogWrapper id="ForumInvitationDialog" params={{}}>
        <ForumInvitationDialogLauncher />
    </StorybookDialogWrapper>
);

ForumInvitationDialog.story = {
    name: "ForumInvitationDialog"
};

export default {
    title: "Misc"
};

export const _SmallNumberPicker = () =>
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
    });

export const BloomAvatars = () =>
    React.createElement(() => {
        return (
            <React.Fragment>
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
            </React.Fragment>
        );
    });

BloomAvatars.story = {
    name: "BloomAvatars"
};

export const _RadioGroup = () =>
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
    });

_RadioGroup.story = {
    name: "RadioGroup"
};

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

export default {
    title: "Misc/Collection Settings"
};

export const WinFormsImitatingSelect = () =>
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
    ));

WinFormsImitatingSelect.story = {
    name: "WinForms imitating Select"
};

export const BookMakingTabPane = () =>
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
    ));

BookMakingTabPane.story = {
    name: "Book Making tab pane"
};

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

export default {
    title: "PlaybackOrderControls"
};

export const PlaybackOrderButtons = () =>
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
    ));

PlaybackOrderButtons.story = {
    name: "PlaybackOrder buttons"
};

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

export default {
    title: "BookInformationCards"
};

export const PreviouslyUploaded = () =>
    React.createElement(() => (
        <BookInfoCard
            title="02. Bigǝ Dinaro Gaana"
            languages={languages1}
            originalUpload={uploadDate}
            lastUpdated={updateDate}
        />
    ));

export const NewUpload = () =>
    React.createElement(() => (
        <BookInfoCard title="02. Foo Bar" languages={languages2} />
    ));

export const SeveralLanguages = () =>
    React.createElement(() => (
        <BookInfoCard title="Foo Bar Extended" languages={languages3} />
    ));

SeveralLanguages.story = {
    name: "Several languages"
};

export default {
    title: "RequiresBloomEnterprise"
};

export const _RequiresBloomEnterpriseNoticeDialog = () =>
    React.createElement(() => <RequiresBloomEnterpriseNoticeDialog />);

_RequiresBloomEnterpriseNoticeDialog.story = {
    name: "RequiresBloomEnterpriseNoticeDialog"
};

export const _RequiresBloomEnterpriseDialog = () =>
    React.createElement(() => (
        <RequiresBloomEnterpriseDialog
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    ));

_RequiresBloomEnterpriseDialog.story = {
    name: "RequiresBloomEnterpriseDialog"
};

export const _RequiresBloomEnterpriseNotice = () =>
    React.createElement(() => <RequiresBloomEnterpriseNotice />);

_RequiresBloomEnterpriseNotice.story = {
    name: "RequiresBloomEnterpriseNotice"
};

export const _RequiresBloomEnterpriseOverlayWrapper = () =>
    React.createElement(() => <RequiresBloomEnterpriseOverlayWrapper />);

_RequiresBloomEnterpriseOverlayWrapper.story = {
    name: "RequiresBloomEnterpriseOverlayWrapper"
};
