import React = require("react");
import * as ReactDOM from "react-dom";
import { IBloomDialogEnvironmentParams } from "../react_components/BloomDialog/BloomDialog";

//Each react component that is referenced from winforms (using ReactControl or ReactDialog) must
// call this:

export function WireUpForWinforms(component: React.FunctionComponent) {
    // The c# ReactControl creates an html page that will call this function.
    (window as any).wireUpRootComponentFromWinforms = (
        root: HTMLElement,
        props?: Object
    ) => {
        props = AddDialogPropsWhenWrappedByWinforms(props);

        ReactDOM.render(React.createElement(component, props, null), root);
    };
}

// These props will not be wanted when we call the dialog component from within browser-land.
function AddDialogPropsWhenWrappedByWinforms(props?: Object) {
    const dialogParamsWhenWrappedByWinforms: IBloomDialogEnvironmentParams = {
        omitOuterFrame: true,
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
