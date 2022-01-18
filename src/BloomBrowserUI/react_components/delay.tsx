import * as React from "react";

// This class shows nothing for showAfterDelay ms, then its children.
// Thanks to goulashsoup on StackOverflow.
class Delayed extends React.Component<
    { showAfterDelay: number },
    { hidden: boolean }
> {
    constructor(props: { showAfterDelay: number }) {
        super(props);
        this.state = { hidden: true };
    }

    componentDidMount() {
        setTimeout(() => {
            this.setState({ hidden: false });
        }, this.props.showAfterDelay);
    }

    render() {
        return this.state.hidden ? "" : this.props.children;
    }
}

export default Delayed;
