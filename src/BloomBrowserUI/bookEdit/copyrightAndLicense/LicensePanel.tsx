/** @jsx jsx **/
/** @jsxFrag React.Fragment */
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useEffect, useState } from "react";
import { RadioGroup, TextField } from "@material-ui/core";

import { kMutedTextGray } from "../../bloomMaterialUITheme";
import { Div, LocalizedString } from "../../react_components/l10nComponents";
import { NoteBox } from "../../react_components/BloomDialog/commonDialogComponents";
import { MuiCheckbox } from "../../react_components/muiCheckBox";
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
    licenseInfo: ILicenseInfo;
    derivativeInfo?: IDerivativeInfo;
    onChange: (isValid: boolean) => void;
}> = props => {
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
        <></>
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
                const creativeCommonsInfo = originalLicense.creativeCommonsInfo;
                return creativeCommonsInfo.allowDerivatives !== "no";
        }
    }

    function handleCcCheckChange() {
        props.onChange(isLicenseValid);
    }

    function handleLicenseTypeChange(licenseType: string) {
        setLicenseType(licenseType);
        setRightsStatement("");
    }

    const [licenseType, setLicenseType] = useState(
        props.licenseInfo.licenseType
    );
    useEffect(() => {
        props.licenseInfo.licenseType = licenseType;
        props.onChange(isLicenseValid);
    }, [licenseType]);

    const [isLicenseValid, setIsLicenseValid] = useState(true);

    useEffect(() => {
        props.onChange(isLicenseValid);
    }, [isLicenseValid]);

    const [rightsStatement, setRightsStatement] = useState(
        props.licenseInfo.rightsStatement
    );

    useEffect(() => {
        props.licenseInfo.rightsStatement = rightsStatement?.trim();
    }, [rightsStatement]);

    useEffect(() => {
        setIsLicenseValid(
            licenseType !== LicenseType.Custom || !!rightsStatement?.trim()
        );
    }, [rightsStatement, licenseType]);

    const enableCheckboxes = licenseType == LicenseType.CreativeCommons;

    if (props.derivativeInfo?.isBookDerivative && !canChangeOriginalLicense()) {
        return (
            // The extra div wrapper here is important because it allows
            // for overriding tab styling for the normal case.
            // See the css for the Tabs component in CopyrightAndLicenseDialog.
            <div>
                <NoteBox addBorder={true}>
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

    return (
        <div>
            <Div l10nKey="License.CopyrightHolderAllows">
                The copyright holder allows others to use the book in this way:
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
                        <MuiCheckbox
                            label={"copy this book for free"}
                            l10nKey="License.CreativeCommons.CopyForFree"
                            disabled={true}
                            checked={
                                licenseType === LicenseType.CreativeCommons
                            }
                            onCheckChanged={() => {}}
                        />
                        <MuiCheckbox
                            label={"use the book in a commercial way"}
                            l10nKey="License.CreativeCommons.AllowCommercial"
                            disabled={!enableCheckboxes}
                            checked={
                                enableCheckboxes &&
                                props.licenseInfo.creativeCommonsInfo
                                    .allowCommercial === "yes"
                            }
                            onCheckChanged={checked => {
                                props.licenseInfo.creativeCommonsInfo.allowCommercial = checked
                                    ? "yes"
                                    : "no";
                                handleCcCheckChange();
                            }}
                        />
                        <MuiCheckbox
                            label={
                                "make new versions of this book, but they must keep the author, illustrator, and other credits"
                            }
                            l10nKey="License.CreativeCommons.ShareAlike"
                            disabled={!enableCheckboxes}
                            checked={
                                enableCheckboxes &&
                                props.licenseInfo.creativeCommonsInfo
                                    .allowDerivatives !== "no"
                            }
                            onCheckChanged={checked => {
                                props.licenseInfo.creativeCommonsInfo.allowDerivatives = checked
                                    ? "yes"
                                    : "no";
                                handleCcCheckChange();
                            }}
                        />
                        <MuiCheckbox
                            label={
                                "apply a different license to new versions of this book"
                            }
                            l10nKey="License.CreativeCommons.DifferentLicense"
                            disabled={
                                !enableCheckboxes ||
                                props.licenseInfo.creativeCommonsInfo
                                    .allowDerivatives === "no"
                            }
                            checked={
                                enableCheckboxes &&
                                props.licenseInfo.creativeCommonsInfo
                                    .allowDerivatives === "yes"
                            }
                            onCheckChanged={checked => {
                                props.licenseInfo.creativeCommonsInfo.allowDerivatives = checked
                                    ? "yes"
                                    : "sharealike";
                                handleCcCheckChange();
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
        <>
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
        </>
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
            onChanged={() => {}}
            value={props.value}
        ></MuiRadio>
    );
};
