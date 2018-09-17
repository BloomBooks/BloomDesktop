import * as React from "react";
import { BloomSelect } from "../../react_components/bloomSelect";
import "./A11yLevelChooser.less";

export interface IProps {
    // I don't know how to express exact types in Typescript here and it doesn't seem worth a lot of effort.
    a11yLevel: any;
}

const levelOptions = [
    {
        value: "none",
        label: "",
        l10nKey: "BookMetadata.a11yLevelNone",
        english: "None",
        comment:
            "indicates that no level of accessibility conformance is claimed"
    },
    {
        value: "wcag-a",
        label: "",
        l10nKey: "BookMetadata.a11yLevelA",
        english: "Level A conformance"
    },
    {
        value: "wcag-aa",
        label: "",
        l10nKey: "BookMetadata.a11yLevelAA",
        english: "Level AA conformance"
    },
    {
        value: "wcag-aaa",
        label: "",
        l10nKey: "BookMetadata.a11yLevelAAA",
        english: "Level AAA conformance"
    }
];

export class A11yLevelChooser extends React.Component<IProps> {
    public render() {
        return (
            <BloomSelect
                currentOption={this.props.a11yLevel}
                options={levelOptions}
                nullOption="none"
                className="a11y-level-chooser"
            />
        );
    }
}

export default A11yLevelChooser;
