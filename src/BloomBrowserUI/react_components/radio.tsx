import * as React from "react";
import * as ReactDOM from "react-dom";
import { ILocalizationProps, LocalizableElement } from "./l10nComponents";
import { ReactElement } from "react";

export interface IRadioProps extends ILocalizationProps {
    value: string; // identifies this radio in set
    className?: string; // class for a div that wraps the input and label, in addition to default "radioButton"
    inputClass?: string; // class for the input element (the radio button itself), in addition to default "radioInput"
    labelClass?: string; // class for the label (text next to the radio button), in addition to default "radioLabel"
    defaultChecked?: boolean; // true if button should be checked; usually controlled by containing RadioGroup
    onSelected?: (string) => void; // passed this button's value when it is clicked; usually used by containing RadioGroup.
}

// A radio button that is localizable.
export class Radio extends LocalizableElement<IRadioProps, {}> {
    constructor(props) {
        super(props);
    }

    public static combineClasses(
        class1: string,
        class2: string | null | undefined
    ): string {
        if (class2) {
            return class1 + " " + class2;
        }
        return class1;
    }
    public render() {
        return (
            <div
                className={Radio.combineClasses(
                    "radioButton",
                    this.props.className
                )}
            >
                <input
                    type="radio"
                    className={Radio.combineClasses(
                        "radioInput",
                        this.props.inputClass
                    )}
                    value={this.props.value}
                    checked={this.props.defaultChecked} // use defaultChecked instead of checked to avoid warning
                    onClick={() => {
                        if (this.props.onSelected) {
                            this.props.onSelected(this.props.value);
                        }
                    }}
                />
                <div
                    className={Radio.combineClasses(
                        "radioLabel",
                        this.props.labelClass
                    )}
                    onClick={() => {
                        if (this.props.onSelected) {
                            this.props.onSelected(this.props.value);
                        }
                    }}
                >
                    {this.getLocalizedContent()}
                </div>
            </div>
        );
    }
}

export interface IRadioGroupProps {
    value: string;
    className?: string;
    onChange?: (string) => void;
}

// A group of radio buttons.
// Usage:
// <RadioGroup onChange={val => this.this.setState({color: val} value={this.state.color}>
//      <Radio i18n="SomeScope.Red" value="red">Red</Radio>
//      <Radio i18n="SomeScope.Green" value="green">Green</Radio>
//      <Radio i18n="SomeScope.Blue" value="blue">Blue</Radio>
// </RadioGroup>
// Radio children may be nested inside other children and combined with non-radio children.
export class RadioGroup extends React.Component<IRadioGroupProps, {}> {
    constructor(props) {
        super(props);
    }
    // This rather tricky function makes a clone of the original children
    // (re-using leaves that it doesn't need to modify)
    // replacing <Radio> elements with a clone that has the required
    // onSelected and defaultChecked properties to function in the group.
    private recursiveFixRadio(children: React.ReactNode): React.ReactNode {
        return React.Children.map(children, child => {
            let childProps: any = {};
            const childElt = child as React.ReactElement<any>;
            if (childElt == null) {
                return child;
            }
            if (childElt.type === Radio) {
                return React.cloneElement(childElt, {
                    onSelected: val => {
                        if (this.props.onChange) {
                            this.props.onChange(val);
                        }
                    },
                    defaultChecked: childElt.props.value === this.props.value
                });
            }
            if (childElt.props) {
                // If it's an element OTHER than a Radio, we'll process it recursively, in case it
                // contains Radio children which need our modifications.
                // This allows other, non Radio elements to be in the RadioGroup, and to contain Radio children.
                childProps.children = this.recursiveFixRadio(
                    childElt.props.children
                );
                return React.cloneElement(childElt, childProps);
            }
            // And if it's an element but somehow has no props at all (if this is even possible),
            // then it has no children so we can safely leave it unchanged.
            return child;
        });
    }
    public render() {
        return (
            <div
                className={Radio.combineClasses(
                    "radioGroup",
                    this.props.className
                )}
            >
                {this.recursiveFixRadio(this.props.children)}
            </div>
        );
    }
}
