/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import FormGroup from "@mui/material/FormGroup";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { useSubscribeToWebSocketForObject } from "../../utils/WebSocketManager";
import { post, useApiBoolean } from "../../utils/bloomApi";
import { kBloomBlue } from "../../utils/colorUtils";
import { useState } from "react";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import Select from "@mui/material/Select";
import MenuItem from "@mui/material/MenuItem";
import { Div } from "../../react_components/l10nComponents";
import { RequiresBloomEnterpriseAdjacentIconWrapper } from "../../react_components/requiresBloomEnterprise";
import { kSelectCss } from "../../bloomMaterialUITheme";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import { ToggleButton, Typography } from "@mui/material";

interface PdfReadyMessage {
    path: string;
}

export const PDFPrintFeaturesGroup: React.FunctionComponent<{
    onChange?: (mode: string) => void;
    onGotPdf: (path: string) => void;
}> = props => {
    useSubscribeToWebSocketForObject(
        "publish",
        "pdfReady",
        (message: PdfReadyMessage) => {
            props.onGotPdf(message.path);
            if (message.path === "") {
                // indicates canceled
                setActiveButton(""); // no pdf, no button selected
            }
        }
    );
    const [activeButton, setActiveButton] = useState("");
    const [allowBooklet] = useApiBoolean("publish/pdf/allowBooklet", true);
    // Eventually this may have more options and will no longer be boolean.
    // But at this point even the underlying model in C# is boolean, so I decided
    // to keep it that way here for now.
    const [cmyk, setCmyk] = useApiBoolean("publish/pdf/cmyk", false);
    const none = useL10n("None", "PublishTab.PdfMaker.None");
    const cmykSwop2 = useL10n(
        "U.S. Web Coated (SWOP) v2",
        "PublishTab.PdfMaker.PdfWithCmykSwopV2"
    );
    const [allowFullBleed] = useApiBoolean("publish/pdf/allowFullBleed", false);

    return (
        <div
            css={css`
                padding-right: 20px;
            `}
        >
            <SettingsGroup
                label={useL10n(
                    "Booklet Mode",
                    "PublishTab.PdfPrint.BookletModes"
                )}
            >
                <div
                    css={css`
                        gap: 10px;
                        display: flex;
                        flex-direction: column;
                        width: 225px;
                    `}
                >
                    <PdfModeButton
                        imgSrc="/bloom/images/simplePages.svg"
                        onClick={() => {
                            post("publish/pdf/simple");
                            setActiveButton("simple");
                            props.onChange?.("simple");
                        }}
                        label="Simple"
                        labelId="PublishTab.OnePagePerPaperRadio"
                        desc="A simple PDF instead of a booklet."
                        descId="PublishTab.OnePagePerPaper-description"
                        selected={activeButton === "simple"}
                    />
                    <BloomTooltip
                        id="cover"
                        placement="left"
                        tipWhenDisabled={{
                            english:
                                "This is disabled because Bloom cannot make booklets using the current size and orientation.",
                            l10nKey: "PublishTab.NoBookletsMessage"
                        }}
                        showDisabled={!allowBooklet}
                    >
                        <PdfModeButton
                            imgSrc="/bloom/images/coverOnly.svg"
                            onClick={() => {
                                post("publish/pdf/cover");
                                setActiveButton("cover");
                                props.onChange?.("cover");
                            }}
                            label="Booklet cover"
                            labelId="PublishTab.CoverOnlyRadio"
                            desc="The cover, ready to print on colored paper."
                            descId="PublishTab.CoverOnly-description"
                            selected={activeButton === "cover"}
                            disabled={!allowBooklet}
                        />
                    </BloomTooltip>
                    <BloomTooltip
                        id="inside"
                        placement="left"
                        tipWhenDisabled={{
                            english:
                                "This is disabled because Bloom cannot make booklets using the current size and orientation.",
                            l10nKey: "PublishTab.NoBookletsMessage"
                        }}
                        showDisabled={!allowBooklet}
                    >
                        <PdfModeButton
                            imgSrc="/bloom/images/insideBookletPages.svg"
                            onClick={() => {
                                post("publish/pdf/pages");
                                setActiveButton("pages");
                                props.onChange?.("pages");
                            }}
                            label="Booklet Insides"
                            labelId="PublishTab.BodyOnlyRadio"
                            desc="The inside pages, re-arranged so that when folded, you get a booklet ready to staple."
                            descId="PublishTab.BodyOnly-description"
                            selected={activeButton === "pages"}
                            disabled={!allowBooklet}
                        />
                    </BloomTooltip>
                </div>
            </SettingsGroup>
            <SettingsGroup
                label={useL10n(
                    "Prepare for Printshop",
                    "PublishTab.PdfPrint.PrintshopOptions"
                )}
            >
                <FormGroup>
                    <RequiresBloomEnterpriseAdjacentIconWrapper>
                        <BloomTooltip
                            showDisabled={!allowFullBleed}
                            // This is a lame explanation... at least it tells us that the problem is not the enterprise status?
                            tipWhenDisabled={{
                                l10nKey:
                                    "PublishTab.PdfMaker.FullBleed.DisableBecauseBookIsNotFullBleed"
                            }}
                        >
                            <ApiCheckbox
                                english="Full Bleed"
                                l10nKey="PublishTab.PdfMaker.FullBleed"
                                apiEndpoint="publish/pdf/fullBleed"
                                disabled={!allowFullBleed}
                                onChange={() => {
                                    // Currently Full Bleed has no effect on Booklet modes.
                                    // There's also no need to immediately generate a PDF if we
                                    // haven't chosen a mode yet. We just want to fix an obsolete
                                    // Simple mode preview if one is showing.
                                    if (activeButton === "simple") {
                                        props.onChange?.(activeButton);
                                        post("publish/pdf/" + activeButton);
                                    }
                                }}
                            />
                        </BloomTooltip>
                    </RequiresBloomEnterpriseAdjacentIconWrapper>
                    <RequiresBloomEnterpriseAdjacentIconWrapper>
                        <div
                            css={css`
                                display: flex;
                                align-items: baseline;
                                gap: 5px;
                                margin-top: 1em; // hack
                            `}
                        >
                            <Div
                                l10nKey="PublishTab.PdfMaker.Cmyk"
                                temporarilyDisableI18nWarning={true}
                            >
                                CMYK
                            </Div>
                            <Select
                                css={css`
                                    ${kSelectCss}
                                `}
                                variant="outlined"
                                value={cmyk ? "cmyk" : "none"}
                                onChange={e => {
                                    const newVal = e.target.value as string;
                                    setCmyk(newVal === "cmyk");
                                    if (activeButton) {
                                        props.onChange?.(activeButton);
                                        post("publish/pdf/" + activeButton);
                                    }
                                }}
                            >
                                <MenuItem value="none">{none}</MenuItem>
                                <MenuItem value="cmyk">{cmykSwop2}</MenuItem>
                            </Select>
                        </div>
                    </RequiresBloomEnterpriseAdjacentIconWrapper>
                </FormGroup>
            </SettingsGroup>
        </div>
    );
};

const PdfModeButton: React.FunctionComponent<{
    onClick?: () => void;
    label: string;
    labelId: string;
    desc: string;
    descId: string;
    imgSrc: string;
    selected: boolean;
    title?: string;
    disabled?: boolean;
}> = props => {
    const title = useL10n(props.label, props.labelId);
    const description = useL10n(props.desc, props.descId);

    return (
        <ToggleButton
            value="foo" // We're not using this, but some value required
            selected={props.selected}
            disabled={props.disabled}
            onChange={() => {
                if (props.onClick && !props.disabled) {
                    props.onClick();
                }
            }}
            css={css`
                background-color: white;

                &.Mui-selected {
                    border: solid 3px ${kBloomBlue};
                    background: white;
                }
                text-transform: none;
            `}
        >
            <div
                css={css`
                    width: 200px;
                    display: flex;
                    flex-direction: column;
                    align-items: center;

                    ${props.disabled ? "opacity: 38%" : ""}
                `}
            >
                <img src={props.imgSrc} />
                <div
                    css={css`
                        color: black;
                        font-weight: bold;
                        margin-top: 5px;
                        margin-bottom: 10px;
                    `}
                >
                    <Typography variant="h6">{title}</Typography>
                </div>
                <div
                    css={css`
                        font-size: smaller;
                        color: #7a7a7a;
                        text-align: start;
                        text-transform: none;
                    `}
                >
                    <Typography variant="body2">{description}</Typography>
                </div>
            </div>
        </ToggleButton>
    );
};
