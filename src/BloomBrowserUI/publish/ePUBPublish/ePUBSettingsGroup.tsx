/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import React = require("react");
import BookMetadataDialog from "../metadata/BookMetadataDialog";
import {
    post,
    useApiBoolean,
    useCanModifyCurrentBook
} from "../../utils/bloomApi";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import { Link } from "../../react_components/link";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { RequiresCheckoutInfo } from "../../react_components/requiresCheckoutInfo";
import { Div } from "../../react_components/l10nComponents";
import {
    FormGroup,
    FormControl,
    MenuItem,
    Select,
    Typography
} from "@mui/material";
import { kSelectCss } from "../../bloomMaterialUITheme";
import { useState } from "react";
import { kBloomDisabledText } from "../../utils/colorUtils";
import { BloomTooltip } from "../../react_components/BloomToolTip";

const epubModes: IEpubMode[] = [
    {
        mode: "fixed",
        label: "Fixed – ePUB 3",
        l10nKey: "PublishTab.Epub.Fixed",
        description:
            "Ask ePUB readers to show pages exactly like you see them in Bloom",
        descriptionL10nKey: "PublishTab.Epub.Fixed.Description"
    },
    {
        mode: "flowable",
        label: "Flowable",
        l10nKey: "PublishTab.Epub.Flowable",
        description:
            "Allow ePUB readers to lay out images and text however they want. The user is more likely to be able to increase font size. Custom page layouts will not look good. This mode is not available if your book has overlay pages (comics).",
        descriptionL10nKey: "PublishTab.Epub.Flowable.Description"
    }
];

export const EPUBSettingsGroup: React.FunctionComponent<{
    onChange: () => void;
    // If we don't adjust this to the current mode, there's an extended display glitch
    // when refreshing.  See https://issues.bloomlibrary.org/youtrack/issue/BL-11043.
    mode: string;
    setMode: (mode: string) => void;
}> = props => {
    const canModifyCurrentBook = useCanModifyCurrentBook();
    const linkCss = "margin-top: 1em !important; display: block;";
    const disabledLinkCss = canModifyCurrentBook
        ? ""
        : `color: ${kBloomDisabledText} !important;`;

    const [isModeDropdownOpen, setIsModeDropdownOpen] = useState(false);

    const [hasOverlays] = useApiBoolean("publish/epub/overlays", true);

    return (
        <div>
            <FormGroup>
                <FormControl
                    variant="outlined"
                    // I'm not sure what else FormGroup and FormControl outlined are good for,
                    // but somehow they cause the blue outline around the control.
                >
                    <div
                        css={css`
                            display: flex;
                            margin-top: 20px;
                            .MuiSelect-root {
                                padding-top: 3px !important;
                                padding-bottom: 4px !important;
                            }
                        `}
                    >
                        <Typography variant="h6">
                            <Div
                                l10nKey="PublishTab.Epub.Mode"
                                l10nComment="a heading for two choices, 'Fixed – ePUB 3' or Flowable"
                            >
                                ePUB mode
                            </Div>
                        </Typography>

                        <div
                            css={css`
                                margin-left: 10px;
                            `}
                        >
                            <Select
                                css={css`
                                    ${kSelectCss}
                                `}
                                variant="outlined"
                                value={props.mode}
                                disabled={false}
                                open={isModeDropdownOpen}
                                onOpen={() => {
                                    setIsModeDropdownOpen(true);
                                }}
                                onClose={() => setIsModeDropdownOpen(false)}
                                onChange={e => {
                                    const newMode = e.target.value as string;
                                    props.setMode(newMode);
                                    props.onChange();
                                }}
                                style={{ width: 145 }}
                                renderValue={f => {
                                    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
                                    const item = epubModes.find(
                                        item => item.mode === f
                                    )!;
                                    return <EpubModeItem {...item} />;
                                }}
                            >
                                {epubModes.map(item => {
                                    const disabled =
                                        hasOverlays && item.mode === "flowable";
                                    const menuItem = (
                                        <MenuItem
                                            value={item.mode}
                                            key={item.mode}
                                            disabled={disabled}
                                        >
                                            <EpubModeItem {...item} />
                                        </MenuItem>
                                    );
                                    // normally, the tooltip is inside of the EpubModeItem, but when disabled,
                                    // the menuItem  doesn't allow the tooltip, so we have to put it outside.
                                    // Meanwhile, if we put it outside of the menuItem when it's not disabled,
                                    // the menuItem stops working.  So we have these two cases.
                                    return disabled ? (
                                        <BloomTooltip
                                            key={item.mode}
                                            showDisabled={true}
                                            tipWhenDisabled={{
                                                l10nKey:
                                                    "PublishTab.Epub.Flowable.DisabledTooltip",
                                                english:
                                                    "This is disabled because an ePUB viewer in flowable mode would not be able to display the overlay pages (comics) in this book."
                                            }}
                                        >
                                            {menuItem}
                                        </BloomTooltip>
                                    ) : (
                                        menuItem
                                    );
                                })}
                            </Select>
                        </div>
                    </div>
                </FormControl>
            </FormGroup>
            <SettingsGroup
                label={useL10n(
                    "Accessibility",
                    "PublishTab.Epub.Accessibility",
                    "Here, the English 'Accessibility' is a common way of referring to technologies that are usable by people with disabilities. With computers, this usually means people with visual impairments. It includes botht he blind and people who might need text to be larger, or who are colorblind, etc."
                )}
            >
                <BloomTooltip
                    showDisabled={props.mode === "fixed"}
                    tipWhenDisabled={{
                        l10nKey: "PublishTab.Epub.IncludeOnPage.Disabled"
                    }}
                >
                    <ApiCheckbox
                        english="Include image descriptions on page"
                        apiEndpoint="publish/epub/imageDescriptionSetting"
                        l10nKey="PublishTab.Epub.IncludeOnPage"
                        disabled={props.mode === "fixed"}
                        forceDisabledValue={false}
                        onChange={props.onChange}
                    />
                </BloomTooltip>

                {/* l10nKey is intentionally not under PublishTab.Epub... we may end up with this link in other places */}
                <Link
                    css={css`
                        ${linkCss}
                    `}
                    id="a11yCheckerLink"
                    l10nKey="AccessibilityCheck.AccessibilityChecker"
                    onClick={() =>
                        post("accessibilityCheck/showAccessibilityChecker")
                    }
                >
                    Accessibility Checker
                </Link>
                <div
                    css={css`
                        display: flex;
                        align-items: center;
                    `}
                >
                    <Link
                        id="bookMetadataDialogLink"
                        css={css`
                            ${linkCss}
                            ${disabledLinkCss}
                        `}
                        l10nKey="PublishTab.BookMetadata"
                        l10nComment="This link opens a dialog box that lets you put in information someone (often a librarian) might use to search for a book with particular characteristics."
                        onClick={() => BookMetadataDialog.show(props.onChange)}
                        disabled={!canModifyCurrentBook}
                    >
                        Book Metadata
                    </Link>
                    <RequiresCheckoutInfo
                        css={css`
                            margin-top: 17px; // needed to align, despite supposed centering
                            margin-left: 5px;
                        `}
                    />
                </div>
            </SettingsGroup>
        </div>
    );
};

interface IEpubMode {
    mode: string; // to pass to bloomApi
    label: string;
    l10nKey: string; // for label
    description: string; // more details
    descriptionL10nKey: string;
}

// Props for the EpubModeOptions component, which displays an instance of IEpubMode
interface IProps extends IEpubMode {
    // The EpubModeItem may (when hovered over) display a popup with more details.
    // Usually the EpubModeItem controls for itself whether this is shown; but we also permit
    // this behavior to work in the controlled component mode, where the client controls
    // its visibility. Technically, the appearance of the popup is controlled by keeping
    // track of which component it is anchored to (if visible), or storing a null if it
    // isn't. If the component is controlled, the client provides changePopupAnchor to
    // receive notification that the control wishes to change this (because it is hovered over),
    // and popupAnchorElement to actually control the presence (and placement) of the popup.
    // The client should normally change popupAnchorElement to whatever changePopupAnchor
    // tells it to, but may also set it to null to force the popup closed. It probably doesn't
    // make sense to set it to anything other than a value received from changePopupAnchor or null.
    changePopupAnchor?: (anchor: HTMLElement | null) => void;
    popupAnchorElement?: HTMLElement | null;
}

const EpubModeItem: React.FunctionComponent<IProps> = props => {
    const id = "mouse-over-popover-" + props.mode;

    return (
        <BloomTooltip
            key={"tooltip-" + props.mode}
            tip={{
                english: props.description,
                l10nKey: props.descriptionL10nKey
            }}
        >
            <div
                css={css`
                    display: flex;
                    min-width: 100px;
                `}
            >
                <Div
                    l10nKey={props.l10nKey}
                    css={css`
                        margin-left: 8px;
                    `}
                    key={props.l10nKey} // prevents stale labels (BL-11179)
                >
                    {props.label}
                </Div>
            </div>
        </BloomTooltip>
    );
};
