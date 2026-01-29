import * as React from "react";
import Select from "react-select";
import theOneLocalizationManager from "../lib/localizationManager/localizationManager";
import * as mobxReact from "mobx-react";

// Only the first two properties of IOption are used by BloomReactSelect.
export interface IOption {
    value: string;
    label: string;
    l10nKey?: string;
    comment?: string;
}
export interface IProps {
    currentOption: IOption; // Only currentOption.value is used in BloomReactSelect.
    options: IOption[];
    nullOption: string; // The IOption .value associated with not having chosen one of the real options
    className: string;
}

// @mobxReact.observer means mobx will automatically track which observables this component uses
// in its render attribute function, and then re-render when they change.
@mobxReact.observer
export class BloomReactSelect extends React.Component<IProps> {
    constructor(props: IProps) {
        super(props);

        this.props.options.map((item) => {
            if (item.l10nKey) {
                theOneLocalizationManager
                    .asyncGetTextAndSuccessInfo(
                        item.l10nKey,
                        item.label,
                        item.comment ? item.comment : "",
                        false,
                    )
                    .done((result) => {
                        item.label = result.text;
                    });
            }
        });
    }

    public render() {
        const selectedOption = this.props.currentOption.value
            ? this.props.options.filter(
                  (x) => x.value === this.props.currentOption.value,
              )[0]
            : this.props.options.filter(
                  (x) => x.value === this.props.nullOption,
              )[0];

        return (
            <Select
                value={selectedOption}
                onChange={(selected) => this.handleChange(selected)}
                options={this.props.options}
                className={this.props.className}
            />
        );
    }

    public handleChange(selectedOption) {
        if (selectedOption.value === this.props.nullOption) {
            this.props.currentOption.value = "";
        } else {
            this.props.currentOption.value = selectedOption.value;
        }
    }
}

export default BloomReactSelect;
