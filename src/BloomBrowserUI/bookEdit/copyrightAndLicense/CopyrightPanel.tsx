/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useEffect, useState } from "react";

import { kMutedTextGray } from "../../bloomMaterialUITheme";
import { NoteBox } from "../../react_components/boxes";
import { Div } from "../../react_components/l10nComponents";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";
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
    onChange: (
        copyrightInfo: ICopyrightInfo,
        useOriginalCopyrightAndLicense: boolean,
        isValid: boolean
    ) => void;
}> = props => {
    const copyrightInfo = JSON.parse(JSON.stringify(props.copyrightInfo)); //clone

    const [isYearValid, setIsYearValid] = useState(false);
    const [isHolderValid, setIsHolderValid] = useState(false);

    const [imageCreator, setImageCreator] = useState(
        copyrightInfo.imageCreator || ""
    );
    const [year, setYear] = useState(
        copyrightInfo.copyrightYear || new Date().getFullYear().toString()
    );
    const [holder, setHolder] = useState(copyrightInfo.copyrightHolder || "");
    const [
        useOriginalCopyrightAndLicense,
        setUseOriginalCopyrightAndLicense
    ] = useState(props.derivativeInfo?.useOriginalCopyright === true);

    const [isSil, setIsSil] = useState(false);

    // These two values are to keep the derivative values if the user
    // turns on `useOriginalCopyrightAndLicense` and then turns it off
    // so we can preserve the values.
    const [yearToPreserve, setYearToPreserve] = useState(year);
    const [holderToPreserve, setHolderToPreserve] = useState(holder);

    useEffect(() => {
        setIsYearValid(isValidYear(year));
    }, [year]);

    function isValidYear(year: string): boolean {
        if (!year) return false;
        year = year.trim();
        if (!year) return false;

        return new RegExp("^\\d\\d\\d\\d$").test(year);
    }

    useEffect(() => {
        setIsHolderValid(isValidHolder(holder));
        setIsSil(holder.includes("SIL"));
    }, [holder]);

    function isValidHolder(holder: string): boolean {
        if (!holder) return false;
        holder = holder.trim();
        return holder.length > 0;
    }

    // This will run on first render.
    // But that's actually what we want because the parent
    // will update the UI based on whether our initial data
    // is valid or not (to enable the OK button, for example).
    useEffect(() => {
        const copyrightInfoToSave: ICopyrightInfo = {
            imageCreator: imageCreator?.trim(),
            copyrightYear: year.trim(),
            copyrightHolder: holder.trim()
        };
        props.onChange(
            copyrightInfoToSave,
            useOriginalCopyrightAndLicense,
            isYearValid && isHolderValid
        );
    }, [
        imageCreator,
        year,
        holder,
        isYearValid,
        isHolderValid,
        useOriginalCopyrightAndLicense
    ]);

    function handleUseOriginalCopyrightAndLicenseChanged(checked: boolean) {
        setUseOriginalCopyrightAndLicense(checked);
        if (checked) {
            setYearToPreserve(year);
            setHolderToPreserve(holder);
            setYear(props.derivativeInfo!.originalCopyrightYear!);
            setHolder(props.derivativeInfo!.originalCopyrightHolder!);
        } else {
            setYear(yearToPreserve);
            setHolder(holderToPreserve);
        }
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
                <React.Fragment>
                    <MuiTextField
                        label={"Illustrator/Photographer"}
                        l10nKey={"Copyright.IllustratorOrPhotographer"}
                        value={imageCreator}
                        onChange={event => {
                            setImageCreator(event.target.value);
                        }}
                        required={false}
                        css={css`
                            margin-bottom: 20px !important;
                        `}
                    />
                </React.Fragment>
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
                    width: 150px; // Enough for slightly longer translations of the label; English only needs 100px
                    margin-bottom: 20px !important;

                    // Hide the up/down control
                    input {
                        -moz-appearance: textfield;
                    }
                `}
            />
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
                css={css`
                    margin-bottom: 11px !important;
                `}
            />
            {isSil && (
                <NoteBox>
                    <div>
                        <Div
                            l10nKey={"Copyright.PublishingAsSIL"}
                            css={css`
                                font-weight: 500;
                            `}
                        >
                            Using "SIL" in a Copyright
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
                                SIL has corporate guidelines around what kinds
                                of materials may be copyrighted by SIL. Please
                                check [this page] in order to ensure that this
                                book qualifies.
                            </PWithLink>
                        </div>
                    </div>
                </NoteBox>
            )}
            {props.derivativeInfo?.isBookDerivative && (
                <React.Fragment>
                    <BloomCheckbox
                        label="Not a translation or new version"
                        checked={useOriginalCopyrightAndLicense}
                        l10nKey="Copyright.NotATranslation"
                        onCheckChanged={checked =>
                            handleUseOriginalCopyrightAndLicenseChanged(
                                checked as boolean
                            )
                        }
                    />
                    <div>
                        <div
                            css={css`
                                margin-left: 28px; // a magic number which happens to align this text with the checkbox label just above it
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
                </React.Fragment>
            )}
        </div>
    );
};
