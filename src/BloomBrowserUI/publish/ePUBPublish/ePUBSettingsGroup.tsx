/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import React = require("react");
import BookMetadataDialog from "../metadata/BookMetadataDialog";
import { BloomApi } from "../../utils/bloomApi";
import { Typography, FormGroup, Tooltip, Popover } from "@material-ui/core";
import { LocalizedString } from "../../react_components/l10nComponents";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import { Link } from "../../react_components/link";
import { SettingsGroup } from "../commonPublish/BasePublishScreen";
import { useL10n } from "../../react_components/l10nHooks";
import { default as InfoIcon } from "@material-ui/icons/InfoOutlined";

export const EPUBSettingsGroup = () => {
    //const [includeImageDescriptionOnPage,setIncludeImageDescriptionOnPage] = BloomApi.useApiBoolean("publish/epub/imageDescriptionSetting", true);
    const [canModifyCurrentBook] = BloomApi.useApiBoolean(
        "common/canModifyCurrentBook",
        false
    );
    const checkoutToEdit = useL10n(
        "Check out the book to use this control",
        "TeamCollection.CheckoutToEdit",
        undefined,
        undefined,
        undefined,
        true
    );

    // controls visibility and placement of the 'tooltip' on the info icon when bookdata is disabled.
    const [anchorEl, setAnchorEl] = React.useState<SVGSVGElement | null>(null);
    const tooltipOpen = Boolean(anchorEl);
    const handlePopoverOpen = (event: React.MouseEvent<SVGSVGElement>) => {
        setAnchorEl(event.currentTarget);
    };
    return (
        <SettingsGroup
            label={useL10n(
                "Accessibility",
                "PublishTab.Epub.Accessibility",
                "Here, the English 'Accessibility' is a common way of referring to technologies that are usable by people with disabilities. With computers, this usually means people with visual impairments. It includes botht he blind and people who might need text to be larger, or who are colorblind, etc."
            )}
        >
            <ApiCheckbox
                english="Include image descriptions on page"
                apiEndpoint="publish/epub/imageDescriptionSetting"
                l10nKey="PublishTab.Epub.IncludeOnPage"
                disabled={false}
            />

            <ApiCheckbox
                english="Use ePUB reader's text size"
                apiEndpoint="publish/epub/removeFontSizesSetting"
                l10nKey="PublishTab.Epub.RemoveFontSizes"
                disabled={false}
                //TODO: priorClickAction={() => this.abortPreview()}
            />
            {/* l10nKey is intentionally not under PublishTab.Epub... we may end up with this link in other places */}
            <Link
                id="a11yCheckerLink"
                l10nKey="AccessibilityCheck.AccessibilityChecker"
                onClick={() =>
                    BloomApi.post("accessibilityCheck/showAccessibilityChecker")
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
                    l10nKey="PublishTab.BookMetadata"
                    l10nComment="This link opens a dialog box that lets you put in information someone (often a librarian) might use to search for a book with particular characteristics."
                    onClick={() => BookMetadataDialog.show()}
                    disabled={!canModifyCurrentBook}
                >
                    Book Metadata
                </Link>
                {canModifyCurrentBook || (
                    <div>
                        <InfoIcon
                            css={css`
                                margin-top: 15px; // needed to align, despite supposed centering
                                margin-left: 5px;
                                color: gray;
                                font-size: 15px;
                            `}
                            onMouseEnter={handlePopoverOpen}
                            onMouseLeave={() => setAnchorEl(null)}
                        ></InfoIcon>
                        <Popover
                            // We use this for a more controllable tooltip than we get with titleAccess on the icon.
                            id={"popover-info-tooltip"}
                            css={css`
                                // This is just an informational popover, we don't need to suppress events outside it.
                                // Even more importantly, we don't want to prevent the parent control from receiving
                                // the mouse-move events that would indicate the mouse is no longer over the anchor
                                // and so the popover should be removed!
                                pointer-events: none;
                            `}
                            // This might be a better way to do it in material-ui 5? Not in V4 API, but in MUI examples.
                            // sx={{
                            //     pointerEvents: 'none',
                            //   }}
                            open={tooltipOpen}
                            anchorEl={anchorEl}
                            anchorOrigin={{
                                vertical: "bottom",
                                horizontal: "center"
                            }}
                            transformOrigin={{
                                // 15 pixels below the bottom (based on anchorOrigin) of the anchor;
                                // leaves room for arrow and a bit of margin.
                                vertical: -15,
                                horizontal: "center"
                            }}
                            onClose={() => setAnchorEl(null)}
                            disableRestoreFocus // most MUI examples have this, not sure what it does.
                        >
                            <div
                                css={css`
                                    padding: 2px 4px;
                                    max-width: 150px;
                                    font-size: smaller;
                                `}
                            >
                                {checkoutToEdit}
                            </div>
                        </Popover>
                    </div>
                )}
            </div>
        </SettingsGroup>
    );
};
