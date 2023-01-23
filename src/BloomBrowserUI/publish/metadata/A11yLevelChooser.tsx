// import { MenuItem } from "@material-ui/core";
import * as React from "react";
import { BloomSelect, IOption } from "../../react_components/bloomSelect";
import { useL10n } from "../../react_components/l10nHooks";
// import WinFormsStyleSelect from "../../react_components/winFormsStyleSelect";

export interface IProps {
    // I don't know how to express exact types in Typescript here and it doesn't seem worth a lot of effort.
    a11yLevel: IOption;
}

export const A11yLevelChooser: React.FunctionComponent<IProps> = props => {
    const levelOptions: IOption[] = [
        {
            value: "none",
            label: useL10n(
                "None",
                "BookMetadata.a11yLevelNone",
                "indicates that no level of accessibility conformance is claimed"
            )
        },
        {
            value: "wcag-a",
            label: "Level A conformance",
            l10nKey: "BookMetadata.a11yLevelA"
        },
        {
            value: "wcag-aa",
            label: "Level AA conformance",
            l10nKey: "BookMetadata.a11yLevelAA"
        },
        {
            value: "wcag-aaa",
            label: "Level AAA conformance",
            l10nKey: "BookMetadata.a11yLevelAAA"
        }
    ];

    return (
        <BloomSelect
            currentOption={props.a11yLevel}
            options={levelOptions}
            nullOptionValue="none"
            className="a11y-level-chooser"
        />
    );
};

export default A11yLevelChooser;
