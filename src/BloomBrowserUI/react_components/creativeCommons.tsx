import * as React from "react";
import { RadioGroup, Radio } from "./radio";
import { Checkbox } from "./checkbox";
import { String, Div } from "./l10n";
import Link from "./link";
import "./creativeCommons.less";

export class CreativeCommonsInfo {
    allowCommercial: string;
    allowDerivatives: string;
    intergovernmentalVersion: boolean;

    constructor() {
        this.allowCommercial = "yes";
        this.allowDerivatives = "yes";
        this.intergovernmentalVersion = false;
    }
}

export interface ICreativeCommonsProps {
    disabled?: boolean;
    content: {
        allowCommercial: string;
        allowDerivatives: string;
        intergovernmentalVersion: boolean;
    };
    onCreativeCommonsChange: (field: string, newValue: string) => void;
}

// export interface ICreativeCommonsState {
//     allowCommercial: string;
//     allowDerivatives: string;
//     intergovernmentalVersion: boolean;
// }

// const InitialState: ICreativeCommonsState = {
//     allowCommercial: "yes",
//     allowDerivatives: "yes",
//     intergovernmentalVersion: false
// };

export default class CreativeCommons extends React.Component<
    ICreativeCommonsProps,
    {}
>
// ICreativeCommonsState
{
    // public readonly state = InitialState;

    getToken() {
        var token = "cc-by-";
        if (this.props.content.allowCommercial === "no") token += "nc-";
        switch (this.props.content.allowDerivatives) {
            case "no":
                token += "nd";
                break;
            case "sharealike":
                token += "sa";
                break;
            case "yes":
                break;
        }
        // Remove trailing dash
        return token.replace(/-\s*$/, "");
    }

    getLicenseImageUrl() {
        return (
            "/bloom/api/copyrightAndLicense/ccImage?token=" + this.getToken()
        );
    }

    // updateCcImage() {
    //     this.setState(this.state);
    // }

    public render() {
        return (
            <div className="creativeCommons">
                <div className="fullWidthRow">
                    <String l10nKey="" className="left">
                        Creative Commons
                    </String>
                    <Link
                        href="http://creativecommons.org/licenses/by-nc/4.0/legalcode#s1i"
                        l10nKey=""
                        className="right"
                    >
                        more info
                    </Link>
                </div>
                <div>
                    <Div l10nKey="">Allow commercial uses of your work?</Div>
                    <RadioGroup
                        value={this.props.content.allowCommercial}
                        onChange={value => {
                            this.props.onCreativeCommonsChange(
                                "allowCommercial",
                                value
                            );
                            // this.props.content.allowCommercial = value;
                            // this.updateCcImage();
                        }}
                        disabled={this.props.disabled}
                    >
                        <Radio value="yes" l10nKey="">
                            Yes
                        </Radio>
                        <Radio value="no" l10nKey="">
                            No
                        </Radio>
                    </RadioGroup>
                </div>
                <div>
                    <Div l10nKey="">Allow modifications of your work?</Div>
                    <RadioGroup
                        value={this.props.content.allowDerivatives}
                        onChange={value => {
                            this.props.onCreativeCommonsChange(
                                "allowDerivatives",
                                value
                            );
                            // this.props.content.allowDerivatives = value;
                            // this.updateCcImage();
                        }}
                        disabled={this.props.disabled}
                    >
                        <Radio value="yes" l10nKey="">
                            Yes
                        </Radio>
                        <Radio value="sharealike" l10nKey="">
                            Yes, as long as others share alike
                        </Radio>
                        <Radio value="no" l10nKey="">
                            No
                        </Radio>
                    </RadioGroup>
                </div>
                <Checkbox
                    name="igvCheckbox"
                    checked={this.props.content.intergovernmentalVersion}
                    l10nKey=""
                    onCheckChanged={value =>
                        (this.props.content.intergovernmentalVersion = value)
                    }
                    disabled={this.props.disabled}
                >
                    Intergovernmental Version
                </Checkbox>
                <img
                    //Using visibility (rather than the hidden prop) keeps the empty space there
                    style={{
                        visibility: this.props.disabled ? "hidden" : "visible"
                    }}
                    className="licenseImage"
                    src={this.getLicenseImageUrl()}
                />
            </div>
        );
    }
}
