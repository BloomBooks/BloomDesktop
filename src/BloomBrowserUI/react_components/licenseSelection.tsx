import * as React from "react";
import { RadioGroup, Radio } from "./radio";
import { String, Div } from "./l10n";
import CreativeCommons, { CreativeCommonsInfo } from "./creativeCommons";
import Link from "./link";
import "./licenseSelection.less";

export class LicenseInfo {
    licenseType: string;
    rightsStatement: string;
    creativeCommonsInfo: CreativeCommonsInfo;
    constructor() {
        this.licenseType = LicenseType.CreativeCommons.toString();
        this.creativeCommonsInfo = new CreativeCommonsInfo();
    }
}

// interface ILicenseSelectionState {
//     licenseType: string;
//     rightsStatement: string;
//     creativeCommonsInfo: CreativeCommonsInfo;
// }

enum LicenseType {
    CreativeCommons = "creativeCommons",
    Contact = "contact",
    Custom = "custom"
}

interface ILicenseSelectionProps {
    content: LicenseInfo;
    handleLicenseSelectionChange: (field: string, newValue: string) => void;
    onCreativeCommonsChange: (field: string, newValue: string) => void;
}

// const InitialState: ILicenseSelectionState = {
//     licenseType: LicenseType.CreativeCommons.toString(),
//     rightsStatement: "",
//     creativeCommonsInfo: new CreativeCommonsInfo()
// };

export default class LicenseSelection extends React.Component<
    ILicenseSelectionProps,
    {}
>
// ILicenseSelectionState
{
    // public readonly state = InitialState;

    public render() {
        return (
            <div className="licenseSelection">
                <String l10nKey="">License</String>
                <RadioGroup
                    value={this.props.content.licenseType.toString()}
                    onChange={newValue =>
                        this.props.handleLicenseSelectionChange(
                            "licenseType",
                            newValue
                        )
                    }
                >
                    <Radio
                        value={LicenseType.CreativeCommons.toString()}
                        l10nKey=""
                    >
                        <CreativeCommons
                            content={this.props.content.creativeCommonsInfo}
                            disabled={
                                this.props.content.licenseType !==
                                LicenseType.CreativeCommons.toString()
                            }
                            onCreativeCommonsChange={(field, newValue) =>
                                this.props.onCreativeCommonsChange(
                                    field,
                                    newValue
                                )
                            }
                        />
                    </Radio>
                    <Radio value={LicenseType.Contact.toString()} l10nKey="">
                        Contact the copyright holder for any permissions
                    </Radio>
                    <Radio
                        value={LicenseType.Custom.toString()}
                        l10nKey=""
                        disabled={true}
                    >
                        Custom
                    </Radio>
                    <div className="fullWidthRow">
                        <Div
                            l10nKey=""
                            hidden={
                                this.props.content.licenseType !==
                                LicenseType.CreativeCommons.toString()
                            }
                        >
                            Additional Requests
                        </Div>
                        <Link
                            l10nKey=""
                            href="http://creativecommons.org/licenses/by-nc/4.0/legalcode#s7a"
                            hidden={
                                this.props.content.licenseType !==
                                    LicenseType.CreativeCommons.toString() ||
                                !this.props.content.rightsStatement
                            }
                        >
                            Not Enforceable
                        </Link>
                    </div>
                    <textarea
                        id="rightsStatement"
                        name="rightsStatement"
                        className="fullWidth"
                        value={this.props.content.rightsStatement}
                        onChange={event => {
                            this.props.handleLicenseSelectionChange(
                                event.target.name,
                                event.target.value
                            );
                        }}
                        disabled={
                            this.props.content.licenseType ===
                            LicenseType.Contact.toString()
                        }
                    />
                </RadioGroup>
            </div>
        );
    }
}
