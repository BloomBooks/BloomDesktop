import * as React from "react";

interface IErrorState {
    error: any;
    errorInfo: any;
}
// This is a very minimal error boundary, but hopefully better than nothing.
export class ErrorBoundary extends React.Component<{}, IErrorState> {
    constructor(props) {
        super(props);
        this.state = { error: null, errorInfo: null };
    }

    public componentDidCatch(error, errorInfo) {
        // Catch errors in any components below and re-render with error message
        this.setState({
            error: error,
            errorInfo: errorInfo
        });
        // You can also log error messages to an error reporting service here
    }

    public render() {
        if (this.state.errorInfo) {
            // Error path
            return (
                <div>
                    <h2>Something went wrong.</h2>
                    <details style={{ whiteSpace: "pre-wrap" }}>
                        {this.state.error && this.state.error.toString()}
                        <br />
                        {this.state.errorInfo.componentStack}
                    </details>
                </div>
            );
        }
        // Normally, just render children
        return this.props.children;
    }
}
