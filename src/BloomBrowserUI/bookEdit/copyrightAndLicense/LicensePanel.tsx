/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useEffect, useState } from "react";
import { RadioGroup, TextField } from "@mui/material";

import { kMutedTextGray } from "../../bloomMaterialUITheme";
import { Div, LocalizedString } from "../../react_components/l10nComponents";
import { NoteBox } from "../../react_components/boxes";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";
import { MuiRadio } from "../../react_components/muiRadio";
import { IDerivativeInfo } from "./CopyrightAndLicenseDialog";
import { useGetLicenseShorthand } from "./LicenseBadge";

export interface ILicenseInfo {
    licenseType: string;
    rightsStatement: string;
    creativeCommonsInfo: ICreativeCommonsInfo;
}

export interface ICreativeCommonsInfo {
    allowCommercial: string;
    allowDerivatives: string;
    intergovernmentalVersion: boolean;
}

export enum LicenseType {
    CreativeCommons = "creativeCommons",
    PublicDomain = "publicDomain",
    Contact = "contact",
    Custom = "custom"
}

// Fields used to modify the license (of a book or image)
export const LicensePanel: React.FunctionComponent<{
    isForBook: boolean; // or image
    licenseInfo: ILicenseInfo;
    derivativeInfo?: IDerivativeInfo;
    onChange: (licenseInfo: ILicenseInfo, isValid: boolean) => void;
}> = props => {
    const licenseInfo = JSON.parse(JSON.stringify(props.licenseInfo)); //clone

    const originalLicenseShorthand = useGetLicenseShorthand(
        props.derivativeInfo?.originalLicense
    );
    const originalLicenseSentence = props.derivativeInfo?.originalLicense ? (
        <LocalizedString
            l10nKey="License.OriginalLicense"
            l10nComment='%0 is a shorthand version of the license such as "CC-BY"'
            l10nParam0={originalLicenseShorthand}
        >
            The original version of this book is %0
        </LocalizedString>
    ) : (
        <React.Fragment></React.Fragment>
    );

    function canChangeOriginalLicense(): boolean {
        const originalLicense = props.derivativeInfo?.originalLicense;
        if (!originalLicense) return true;

        switch (originalLicense.licenseType) {
            case LicenseType.PublicDomain:
                return true;
            case LicenseType.Contact:
            case LicenseType.Custom:
                return false;
            default:
                return (
                    originalLicense.creativeCommonsInfo.allowDerivatives !==
                    "no"
                );
        }
    }

    function reportChange() {
        props.onChange(licenseInfo, isLicenseValid);
    }

    function handleLicenseTypeChange(licenseType: string) {
        setLicenseType(licenseType);
        setRightsStatement("");
    }

    const [licenseType, setLicenseType] = useState(licenseInfo.licenseType);
    useEffect(() => {
        licenseInfo.licenseType = licenseType;
        reportChange();
    }, [licenseType]);

    const [isLicenseValid, setIsLicenseValid] = useState(true);

    useEffect(() => {
        reportChange();
    }, [isLicenseValid]);

    const [rightsStatement, setRightsStatement] = useState(
        licenseInfo.rightsStatement
    );

    useEffect(() => {
        licenseInfo.rightsStatement = rightsStatement?.trim();
        reportChange();
    }, [rightsStatement]);

    useEffect(() => {
        setIsLicenseValid(
            licenseType !== LicenseType.Custom || !!rightsStatement?.trim()
        );
    }, [rightsStatement, licenseType]);

    const isCCLicense = licenseType == LicenseType.CreativeCommons;

    if (props.derivativeInfo?.isBookDerivative && !canChangeOriginalLicense()) {
        return (
            // The extra div wrapper here is important because it allows
            // for overriding tab styling for the normal case.
            // See the css for the Tabs component in CopyrightAndLicenseDialog.
            <div>
                <NoteBox>
                    <div>
                        <Div l10nKey="License.CannotChangeLicense">
                            The license of the original book does not allow
                            changing the license in new versions.
                        </Div>
                        <div
                            css={css`
                                font-size: 0.75em;
                                margin-top: 3px;
                            `}
                        >
                            {originalLicenseSentence}
                        </div>
                    </div>
                </NoteBox>
            </div>
        );
    }

    function getBookOrImage(): string {
        return props.isForBook ? "book" : "image";
    }
    function getL10nIdForBookOrImage(idBase: string): string {
        return idBase + (props.isForBook ? ".Book" : ".Image");
    }

    return (
        <div>
            <Div
                l10nKey={getL10nIdForBookOrImage(
                    "License.CopyrightHolderAllows"
                )}
            >
                The copyright holder allows others to use the {getBookOrImage()}{" "}
                in this way:
            </Div>

            <RadioGroup
                value={licenseType}
                defaultValue="creativeCommons"
                onChange={e => handleLicenseTypeChange(e.target.value)}
                name="license-selection-radio-group"
            >
                <Radio
                    value={"publicDomain"}
                    label="Anyone can use it however they want."
                    l10nKey="License.PublicDomain"
                />
                <Radio
                    value={"creativeCommons"}
                    label="Everyone is allowed to:"
                    l10nKey="License.CreativeCommons.Intro"
                />
                <SubPanel>
                    <div
                        css={css`
                            max-width: 350px;
                        `}
                    >
                        <BloomCheckbox
                            label={`copy this ${getBookOrImage()} for free`}
                            l10nKey={getL10nIdForBookOrImage(
                                "License.CreativeCommons.CopyForFree"
                            )}
                            disabled={true}
                            checked={
                                licenseType === LicenseType.CreativeCommons
                            }
                            onCheckChanged={() => {
                                // No handler needed because the checkbox is disabled.
                            }}
                        />
                        <BloomCheckbox
                            label={`use the ${getBookOrImage()} in a commercial way`}
                            l10nKey={getL10nIdForBookOrImage(
                                "License.CreativeCommons.AllowCommercial"
                            )}
                            disabled={!isCCLicense}
                            checked={
                                isCCLicense &&
                                licenseInfo.creativeCommonsInfo
                                    .allowCommercial === "yes"
                            }
                            onCheckChanged={checked => {
                                licenseInfo.creativeCommonsInfo.allowCommercial = checked
                                    ? "yes"
                                    : "no";
                                reportChange();
                            }}
                        />
                        {/* These two check boxes govern the allowDerivatives value. Both apply only to CC licenses.
                        The first determines whether derivatives are allowed at all.
                        If so, the second is enabled and determines whether allowDerivatives should be "sharealike" or simply "yes" */}
                        <BloomCheckbox
                            label={
                                props.isForBook
                                    ? "make new versions of this book, but they must keep the author, illustrator, and other credits"
                                    : "make new versions of this image, but they must keep the illustrator and other credits"
                            }
                            l10nKey={getL10nIdForBookOrImage(
                                "License.CreativeCommons.ShareAlike"
                            )}
                            disabled={!isCCLicense}
                            checked={
                                isCCLicense &&
                                licenseInfo.creativeCommonsInfo
                                    .allowDerivatives !== "no"
                            }
                            onCheckChanged={checked => {
                                licenseInfo.creativeCommonsInfo.allowDerivatives = checked
                                    ? "yes"
                                    : "no";
                                reportChange();
                            }}
                        />
                        <BloomCheckbox
                            label={`apply a different license to new versions of this ${getBookOrImage()}`}
                            l10nKey={getL10nIdForBookOrImage(
                                "License.CreativeCommons.DifferentLicense"
                            )}
                            disabled={
                                !isCCLicense ||
                                licenseInfo.creativeCommonsInfo
                                    .allowDerivatives === "no"
                            }
                            checked={
                                isCCLicense &&
                                licenseInfo.creativeCommonsInfo
                                    .allowDerivatives === "yes"
                            }
                            onCheckChanged={checked => {
                                licenseInfo.creativeCommonsInfo.allowDerivatives = checked
                                    ? "yes"
                                    : "sharealike";
                                reportChange();
                            }}
                        />
                    </div>
                    {licenseType === LicenseType.CreativeCommons && (
                        <CustomNote
                            // tslint:disable-next-line:max-line-length
                            label="You may use this space to clarify or grant additional permissions (for example, that translations are allowed), but not to alter the license."
                            l10nKey="License.CreativeCommons.Additional"
                            value={rightsStatement}
                            onChange={setRightsStatement}
                        ></CustomNote>
                    )}
                </SubPanel>
                <Radio
                    value={"contact"}
                    // tslint:disable-next-line:max-line-length
                    label="No one can modify or copy it without explicit permission. We will not be sharing it via Bloom Reader or BloomLibrary.org."
                    l10nKey="License.AllRightsReserved.Description"
                    css={css`
                        margin-top: 20px;
                    `}
                />
                <Radio value={"custom"} label="Other" l10nKey="Common.Other" />
                {licenseType === LicenseType.Custom && (
                    <SubPanel>
                        <CustomNote
                            // tslint:disable-next-line:max-line-length
                            label="Avoid writing a custom license if at all possible. They are difficult to write, interpret, and enforce."
                            l10nKey="License.Custom.AvoidIfPossible"
                            value={rightsStatement}
                            onChange={setRightsStatement}
                        ></CustomNote>
                    </SubPanel>
                )}
            </RadioGroup>
            {props.derivativeInfo?.isBookDerivative && (
                <div
                    css={css`
                        margin-top: 30px;
                        color: ${kMutedTextGray};
                    `}
                >
                    {originalLicenseSentence}
                </div>
            )}
        </div>
    );
};

// Just a simple wrapper to indent components
const SubPanel: React.FunctionComponent<{}> = props => {
    return (
        <div
            css={css`
                margin-left: 30px;
            `}
        >
            {props.children}
        </div>
    );
};

const CustomNote: React.FunctionComponent<{
    label: string;
    l10nKey: string;
    value: string;
    onChange: (value: string) => void;
}> = props => {
    return (
        <React.Fragment>
            <Div
                l10nKey={props.l10nKey}
                css={css`
                    font-size: 0.75em;
                `}
            >
                {props.label}
            </Div>
            <TextField // Not using MuiTextField because there is no label (to localize)
                variant="outlined"
                multiline
                rows={2}
                fullWidth
                value={props.value}
                onChange={e => props.onChange(e.target.value)}
            />
        </React.Fragment>
    );
};

const Radio: React.FunctionComponent<{
    label: string;
    l10nKey: string;
    value: string;
}> = props => {
    return (
        <MuiRadio
            label={props.label}
            l10nKey={props.l10nKey}
            value={props.value}
        />
    );
};
