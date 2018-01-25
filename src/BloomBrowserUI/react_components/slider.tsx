import * as React from "react";
import * as ReactDOM from "react-dom";

export interface ISliderProps {
    id: string;
    value: number;
    onChange: (number) => void;
    enabled: boolean;
}

export class Slider extends React.Component<ISliderProps> {
    constructor(props: ISliderProps) {
        super(props);
    }

    $slider: JQuery; // the root element controlled by jquery.slider(), set in componentDidMount()

    render() {
        return (
            <div ref="slider" id={this.props.id} />
        );
    }

    componentDidMount() {
        this.$slider = $(this.refs.slider);
        this.$slider.slider({
            value: this.props.value,
            slide: (event, ui) => {
                this.props.onChange(ui.value);
            },
            disabled: !this.props.enabled
        });
    }

    // We never 'update' in the sense of re-running Render in true React style.
    // Instead, jquery operates the wrapped div in its usual way, as set up in componentDidMount.
    // If props change, our implementation of componentWillReceiveProps() updates the jquery component.
    shouldComponentUpdate() {
        return false;
    }

    componentWillReceiveProps(nextProps: ISliderProps) {
        if (nextProps.enabled !== this.props.enabled) {
            this.$slider.slider("option", "disabled", !nextProps.enabled);
        }
        if (nextProps.value !== this.props.value) {
            this.$slider.slider("option", "value", nextProps.value);
        }
    }
}
