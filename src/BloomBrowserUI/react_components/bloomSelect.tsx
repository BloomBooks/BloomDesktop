import * as React from "react";
import Select from "react-select";
import theOneLocalizationManager from "../lib/localizationManager/localizationManager";

export interface IProps {
    // I don't know how to express exact types in Typescript here and it doesn't seem worth a lot of effort.
    currentOption: any; // { key, value }, only value is used here
    options: any; // { value, label, l10nKey, comment }, first two used by Select.
    nullOption: string; // option .value key associated with not having chosen one of the real options
    className: string;
}

export class BloomSelect extends React.Component<IProps> {
    constructor(props) {
        super(props);

        this.props.options.map(item => {
            if (item.l10nKey) {
                theOneLocalizationManager
                    .asyncGetTextAndSuccessInfo(
                        item.l10nKey,
                        item.label,
                        item.comment ? item.comment : ""
                    )
                    .done(result => {
                        item.label = result.text;
                    });
            }
        });
    }

    public render() {
        const selectedOption = this.props.currentOption.value
            ? this.props.options.filter(
                  x => x.value === this.props.currentOption.value
              )[0]
            : this.props.options.filter(
                  x => x.value === this.props.nullOption
              )[0];

        return (
            <Select
                value={selectedOption}
                onChange={selectedOption => this.handleChange(selectedOption)}
                options={this.props.options}
                className={this.props.className}
            />
        );
    }

    public handleChange(selectedOption) {
        this.props.currentOption.value = selectedOption.value;
        if (selectedOption.value == this.props.nullOption)
            this.props.currentOption.value = "";
    }
}

export default BloomSelect;
