/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import FormGroup from "@mui/material/FormGroup";
import { SettingsGroup } from "../commonPublish/PublishScreenBaseComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { useSubscribeToWebSocketForObject } from "../../utils/WebSocketManager";
import { post, useApiBoolean } from "../../utils/bloomApi";
import { BloomTooltip } from "../../react_components/BloomToolTip";
import { kBloomBlue, kBloomDisabledText } from "../../utils/colorUtils";
import { useState } from "react";
import { ApiCheckbox } from "../../react_components/ApiCheckbox";
import Select from "@mui/material/Select";
import MenuItem from "@mui/material/MenuItem";
import { Div, H1, H3, Span } from "../../react_components/l10nComponents";
import { RequiresBloomEnterpriseAdjacentIconWrapper } from "../../react_components/requiresBloomEnterprise";
import { kSelectCss } from "../../bloomMaterialUITheme";

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

    // These are used to allow the tooltips to appear only when booklets are disabled.
    const [
        coverTooltipAnchor,
        setCoverTooltipAnchor
    ] = useState<HTMLElement | null>(null);
    const [
        insideTooltipAnchor,
        setInsideTooltipAnchor
    ] = useState<HTMLElement | null>(null);

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
                <FormGroup>
                    <FeatureButton
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
                        id="cover-disabled"
                        side="left"
                        sideVerticalOrigin={30}
                        sideHorizontalOrigin={10}
                        arrowLoc="middle"
                        tooltipL10nKey="PublishTab.NoBookletsMessage"
                        tooltipText="This is disabled because Bloom cannot make booklets using the current size and orientation."
                        popupAnchorElement={coverTooltipAnchor}
                        changePopupAnchor={val =>
                            allowBooklet
                                ? setCoverTooltipAnchor(null) // don't show if enabled
                                : setCoverTooltipAnchor(val)
                        }
                    >
                        <FeatureButton
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
                        id="cover-disabled"
                        side="left"
                        sideVerticalOrigin={30}
                        sideHorizontalOrigin={10}
                        arrowLoc="middle"
                        tooltipL10nKey="PublishTab.NoBookletsMessage"
                        tooltipText="This is disabled because Bloom cannot make booklets using the current size and orientation."
                        popupAnchorElement={insideTooltipAnchor}
                        changePopupAnchor={val =>
                            allowBooklet
                                ? setInsideTooltipAnchor(null) // don't show if enabled
                                : setInsideTooltipAnchor(val)
                        }
                    >
                        <FeatureButton
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
                </FormGroup>
            </SettingsGroup>
            <SettingsGroup
                label={useL10n(
                    "Prepare for Printshop",
                    "PublishTab.PdfPrint.PrintshopOptions"
                )}
            >
                <FormGroup>
                    <RequiresBloomEnterpriseAdjacentIconWrapper>
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

// Review: should this be a material-ui button? Have to fight mui for many of the
// CSS values such as text color and capitalization, so it seems more trouble than
// it's worth, but maybe there are button-like behaviors I'm missing?
const FeatureButton: React.FunctionComponent<{
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
        <div
            css={css`
                background-color: white;
                border-radius: 3px;
                // I borrowed this from stack overflow; it seems to produce a similar
                // look to the mock-up. Not sure if we have something somewhere that
                // it should match more exactly.
                box-shadow: 0 1px 2px hsla(0, 0%, 0%, 0.05),
                    0 1px 4px hsla(0, 0%, 0%, 0.05),
                    0 2px 8px hsla(0, 0%, 0%, 0.05);
                padding: 10px 5px;
                // if this becomes a more public component, I'd prefer margin to be controlled
                // by the client.
                margin-bottom: 10px;
                margin-left: auto;
                margin-right: auto;
                width: 210px;
                ${props.selected ? "border: solid 3px " + kBloomBlue : ""}
                ${props.disabled ? "color: " + kBloomDisabledText : ""}
            `}
            onClick={() => {
                if (props.onClick && !props.disabled) {
                    props.onClick();
                }
            }}
            title={props.title}
        >
            <div
                css={css`
                    width: 200px;
                    display: flex;
                    flex-direction: column;
                    align-items: center;
                `}
            >
                <img
                    css={css`
                        // This matches how MUI shows some disabled things.
                        ${props.disabled ? "opacity: 38%" : ""}
                    `}
                    src={props.imgSrc}
                />
                <div
                    css={css`
                        font-weight: bold;
                        margin-top: 5px;
                        margin-bottom: 10px;
                    `}
                >
                    {title}
                </div>
                <div
                    css={css`
                        font-size: smaller;
                        color: #7a7a7a;
                    `}
                >
                    {description}
                </div>
            </div>
        </div>
    );
};
