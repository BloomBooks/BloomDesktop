import * as React from "react";

// This class shows nothing for waitBeforeShow ms, then its children.
// Thanks to goulashsoup on StackOverflow.
class Delayed extends React.Component<
    { waitBeforeShow: number },
    { hidden: boolean }
> {
    constructor(props: { waitBeforeShow: number }) {
        super(props);
        this.state = { hidden: true };
    }

    componentDidMount() {
        setTimeout(() => {
            this.setState({ hidden: false });
        }, this.props.waitBeforeShow);
    }

    render() {
        return this.state.hidden ? "" : this.props.children;
    }
}

export default Delayed;
