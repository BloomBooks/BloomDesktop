import React = require("react");
import * as ReactDOM from "react-dom";
import { IBloomDialogEnvironmentParams } from "../react_components/BloomDialog/BloomDialog";
import { lightTheme } from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";

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
        // Simulating a click on the root element shouldn't have any effects through
        // any click handlers that our React code might add since we haven't rendered
        // react into the element yet. But it has one effect I haven't found any other
        // way to bring about: it puts focus into the root document of this component.
        // Since the component is about to become the root of a control, often the root
        // of a whole window, this is nearly always a good thing. For one thing, it
        // allows using Escape to close a BloomDialog, and makes it work when we use
        // focus() to put the focus on some particular control after rendering.
        // If we find some case where it isn't desired, we can figure out some way to
        // make it optional.
        root.click();
        props = AddDialogPropsWhenWrappedByWinforms(props);
        const c = React.createElement(component, props, null);
        ReactDOM.render(<ThemedRoot>{c}</ThemedRoot>, root);
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
