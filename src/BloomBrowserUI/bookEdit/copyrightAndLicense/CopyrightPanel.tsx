/** @jsx jsx **/
/** @jsxFrag React.Fragment */
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useEffect, useState } from "react";

import { kMutedTextGray } from "../../bloomMaterialUITheme";
import { NoteBox } from "../../react_components/BloomDialog/commonDialogComponents";
import { Div } from "../../react_components/l10nComponents";
import { MuiCheckbox } from "../../react_components/muiCheckBox";
import { MuiTextField } from "../../react_components/muiTextField";
import { PWithLink } from "../../react_components/pWithLink";
import { IDerivativeInfo } from "./CopyrightAndLicenseDialog";

export interface ICopyrightInfo {
    imageCreator?: string;
    copyrightYear: string;
    copyrightHolder: string;
}

// Fields used to modify the copyright (of a book or image)
export const CopyrightPanel: React.FunctionComponent<{
    isForBook: boolean;
    derivativeInfo?: IDerivativeInfo;
    copyrightInfo: ICopyrightInfo;
    onChange: (isValid: boolean) => void;
}> = props => {
    const [isYearValid, setIsYearValid] = useState(false);
    const [isHolderValid, setIsHolderValid] = useState(false);
    const [imageCreator, setImageCreator] = useState(
        props.copyrightInfo.imageCreator || ""
    );
    const [year, setYear] = useState(
        props.copyrightInfo.copyrightYear || new Date().getFullYear().toString()
    );
    const [holder, setHolder] = useState(
        props.copyrightInfo.copyrightHolder || ""
    );
    const [isSil, setIsSil] = useState(false);
    const [
        useOriginalCopyrightAndLicense,
        setUseOriginalCopyrightAndLicense
    ] = useState(props.derivativeInfo?.useOriginalCopyright === true);

    // These two values are to keep the derivative values if the user
    // turns on `useOriginalCopyrightAndLicense` and then turns it off
    // so we can preserve the values.
    const [yearToPreserve, setYearToPreserve] = useState(year);
    const [holderToPreserve, setHolderToPreserve] = useState(holder);

    useEffect(() => {
        setIsYearValid(isValidYear(year));
        props.copyrightInfo.copyrightYear = year?.trim();
    }, [year]);

    function isValidYear(year: string): boolean {
        if (!year) return false;
        year = year.trim();
        if (!year) return false;

        const yearAsNum = Number.parseInt(year, 10);
        if (Number.isNaN(yearAsNum)) return false;

        return yearAsNum >= 1900 && yearAsNum <= 2100;
    }

    useEffect(() => {
        setIsHolderValid(isValidHolder(holder));
        setIsSil(holder.toLowerCase().match(/\bsil\b/) != null);
        props.copyrightInfo.copyrightHolder = holder?.trim();
    }, [holder]);

    function isValidHolder(holder: string): boolean {
        if (!holder) return false;
        holder = holder.trim();
        return holder.length > 0;
    }

    useEffect(() => {
        props.copyrightInfo.imageCreator = imageCreator;
    }, [imageCreator]);

    useEffect(() => {
        props.onChange(isYearValid && isHolderValid);
    }, [isYearValid, isHolderValid, useOriginalCopyrightAndLicense]);

    function handleUseOriginalCopyrightAndLicenseChanged(checked: boolean) {
        setUseOriginalCopyrightAndLicense(checked);
        props.derivativeInfo!.useOriginalCopyright = checked;
        if (checked) {
            setYearToPreserve(year);
            setHolderToPreserve(holder);
            setYear(props.derivativeInfo?.originalCopyrightYear!);
            setHolder(props.derivativeInfo?.originalCopyrightHolder!);
        } else {
            setYear(yearToPreserve);
            setHolder(holderToPreserve);
        }
    }

    function getVerticalSpacer(numPixels: number) {
        return (
            <div
                css={css`
                    height: ${numPixels}px;
                `}
            ></div>
        );
    }

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                margin-top: 5px; // fudge for the text field label
            `}
        >
            {!props.isForBook && (
                <>
                    <MuiTextField
                        label={"Illustrator/Photographer"}
                        l10nKey={"Copyright.IllustratorOrPhotographer"}
                        value={imageCreator}
                        onChange={event => {
                            setImageCreator(event.target.value);
                        }}
                        required={false}
                    />
                    {getVerticalSpacer(20)}
                </>
            )}
            <MuiTextField
                label="Copyright Year"
                l10nKey={"Copyright.CopyrightYear"}
                type="number"
                value={year}
                onChange={event => {
                    setYear(event.target.value);
                }}
                disabled={useOriginalCopyrightAndLicense}
                required={true}
                error={!isYearValid}
                css={css`
                    width: 100px;
                `}
            />
            {getVerticalSpacer(20)}
            <MuiTextField
                label="Copyright Holder"
                l10nKey={"Copyright.CopyrightHolder"}
                value={holder}
                onChange={event => {
                    setHolder(event.target.value);
                }}
                disabled={useOriginalCopyrightAndLicense}
                required={true}
                error={!isHolderValid}
            />
            {getVerticalSpacer(11)}
            {isSil && (
                <NoteBox addBorder={true}>
                    <div>
                        <Div l10nKey={"Copyright.PublishingAsSIL"}>
                            Publishing as SIL
                        </Div>
                        <div
                            css={css`
                                font-size: 0.75em;
                                margin-top: 5px;
                                p {
                                    margin-block-end: 0 !important; // override DialogMiddle setting and browser default
                                }
                            `}
                        >
                            <PWithLink
                                l10nKey="Copyright.FollowSILGuidelines"
                                l10nComment="The text inside the [square brackets] will become a link to a website."
                                href={
                                    "https://docs.bloomlibrary.org/sil-corporate-guidelines"
                                }
                            >
                                Before publishing as SIL, ensure that you follow
                                [SIL corporate guidelines].
                            </PWithLink>
                        </div>
                    </div>
                </NoteBox>
            )}
            {props.derivativeInfo?.isBookDerivative && (
                <>
                    <MuiCheckbox
                        label="Not a translation or new version"
                        checked={useOriginalCopyrightAndLicense}
                        l10nKey="Copyright.NotATranslation"
                        onCheckChanged={checked =>
                            handleUseOriginalCopyrightAndLicenseChanged(
                                checked as boolean
                            )
                        }
                    ></MuiCheckbox>
                    <div>
                        <div
                            css={css`
                                margin-left: 28px; // a magic number which happens to align things
                            `}
                        >
                            <div
                                css={css`
                                    font-size: 10px;
                                    color: ${kMutedTextGray};
                                    margin-top: -9px;
                                `}
                            >
                                <Div l10nKey={"Copyright.UseOriginalCopyright"}>
                                    Continue to use the same copyright and
                                    license as the original book:
                                </Div>
                                <div>
                                    {
                                        props.derivativeInfo
                                            .originalCopyrightAndLicenseText
                                    }
                                </div>
                            </div>
                        </div>
                    </div>
                </>
            )}
        </div>
    );
};
