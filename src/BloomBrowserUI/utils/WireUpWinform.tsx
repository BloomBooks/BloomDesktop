import React = require("react");
import * as ReactDOM from "react-dom";
import { IBloomDialogEnvironmentParams } from "../react_components/BloomDialog/BloomDialog";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";

//Each react component that is referenced from winforms (using ReactControl or ReactDialog) must
// call this:

export function WireUpForWinforms(component: React.FunctionComponent) {
    // The c# ReactControl creates an html page that will call this function.
    (window as any).wireUpRootComponentFromWinforms = (
        root: HTMLElement,
        props?: Object
    ) => {
        props = AddDialogPropsWhenWrappedByWinforms(props);
        const c = React.createElement(component, props, null);
        ReactDOM.render(<ThemedRoot>{c}</ThemedRoot>, root);
    };
}

const ThemedRoot: React.FunctionComponent<{}> = props => {
    return <ThemeProvider theme={theme}>{props.children}</ThemeProvider>;
};

// These props will not be wanted when we call the dialog component from within browser-land.
function AddDialogPropsWhenWrappedByWinforms(props?: Object) {
    const dialogParamsWhenWrappedByWinforms: IBloomDialogEnvironmentParams = {
        dialogFrameProvidedExternally: true,
        initiallyOpen: true
    };
    return props
        ? {
              dialogEnvironment: dialogParamsWhenWrappedByWinforms,
              ...props
          }
        : {
              dialogEnvironment: dialogParamsWhenWrappedByWinforms
          };
}
