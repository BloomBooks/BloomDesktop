import * as React from "react";
import Select from "react-select";
import "./A11yLevelChooser.less";

export interface IProps {
    // We don't know or care what the top level elements are to this. We will show a row for each
    // of the top level entries that we find.
    // However the "value" of each entry must itself be an object of type {type:___, value:___}.
    // I don't know if it is possible to express that in Typescript and it doesn't seem worth a lot of effort.
    a11yLevel: any;
    translatedOptions: any;
}

export class A11yLevelChooser extends React.Component<IProps> {
    public render() {
        const options = [
            {
                value: "none",
                label: this.props.translatedOptions.a11yLevelNone
            },
            { value: "wcag-a", label: this.props.translatedOptions.a11yLevelA },
            {
                value: "wcag-aa",
                label: this.props.translatedOptions.a11yLevelAA
            },
            {
                value: "wcag-aaa",
                label: this.props.translatedOptions.a11yLevelAAA
            }
        ];

        const selectedOption = this.props.a11yLevel.value
            ? options.filter(x => x.value === this.props.a11yLevel.value)[0]
            : options.filter(x => x.value === "none")[0];

        return (
            <Select
                value={selectedOption}
                onChange={selectedOption => this.handleChange(selectedOption)}
                options={options}
                className="a11y-level-chooser"
            />
        );
    }

    public handleChange(selectedOption) {
        this.props.a11yLevel.value = selectedOption.value;
        if (selectedOption.value == "none") this.props.a11yLevel.value = "";
    }
}

export default A11yLevelChooser;
