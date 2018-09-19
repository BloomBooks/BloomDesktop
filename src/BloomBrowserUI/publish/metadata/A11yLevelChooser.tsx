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
        label: "None",
        l10nKey: "BookMetadata.a11yLevelNone",
        comment:
            "indicates that no level of accessibility conformance is claimed"
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
