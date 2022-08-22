import React = require("react");
import * as ReactDOM from "react-dom";
import { IBloomDialogEnvironmentParams } from "../react_components/BloomDialog/BloomDialogPlumbing";
import { lightTheme } from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { hookupLinkHandler } from "./linkHandler";

/**
 * Each react component that is referenced from winforms (using ReactControl or ReactDialog) must
 * call this:
 * @param component: A React function component. Most likely should be VoidFunctionComponent (no children),
 * but probably theoretically possible (although difficult) to pass children from WinForms, so allow FunctionComponent (implicit children) too
 */
export function WireUpForWinforms(
    component: React.VoidFunctionComponent | React.FunctionComponent
) {
    // The c# ReactControl creates an html page that will call this function.
    (window as any).wireUpRootComponentFromWinforms = (
        root: HTMLElement,
        props?: Object
    ) => {
        props = AddDialogPropsWhenWrappedByWinforms(props);
        const c = React.createElement(component, props, null);
        ReactDOM.render(<ThemedRoot>{c}</ThemedRoot>, root);
        hookupLinkHandler();
    };
}

const ThemedRoot: React.FunctionComponent<{}> = props => {
    return <ThemeProvider theme={lightTheme}>{props.children}</ThemeProvider>;
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
