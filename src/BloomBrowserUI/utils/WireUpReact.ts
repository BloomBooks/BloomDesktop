import React = require("react");
import * as ReactDOM from "react-dom";
import { TeamCollectionSettingsPanel } from "../teamCollection/TeamCollectionSettingsPanel";
import { TeamCollectionDialog } from "../teamCollection/TeamCollectionDialog";
import { AutoUpdateSoftwareDialog } from "../react_components/AutoUpdateSoftwareDialog";
import { ProblemDialog } from "../problemDialog/ProblemDialog";
import { ProgressDialog } from "../react_components/IndependentProgressDialog";
import { PerformanceLogControl } from "../react_components/Performance/PerformanceLogControl";

// this is a bummer... haven't figured out how to do a lookup just from the string... have to have this map
const knownComponents = {
    TeamCollectionSettingsPanel: TeamCollectionSettingsPanel,
    TeamCollectionDialog: TeamCollectionDialog,
    AutoUpdateSoftwareDialog: AutoUpdateSoftwareDialog,
    ProblemDialog: ProblemDialog,
    ProgressDialog: ProgressDialog,
    PerformanceLogControl: PerformanceLogControl
};

// This is called from an html file created in the c# ReactControl class.
(window as any).wireUpReact = (
    root: HTMLElement,
    reactComponentName: string
) => {
    ReactDOM.render(
        React.createElement(knownComponents[reactComponentName], {}, null),
        root
    );
};
