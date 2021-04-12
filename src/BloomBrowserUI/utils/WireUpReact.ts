import React = require("react");
import * as ReactDOM from "react-dom";
import { CollectionTabPane } from "../collectionTab/CollectionTabPane";
import { TeamCollectionSettingsPanel } from "../teamCollection/TeamCollectionSettingsPanel";
import { TeamCollectionDialog } from "../teamCollection/TeamCollectionDialog";
import { AutoUpdateSoftwareDialog } from "../react_components/AutoUpdateSoftwareDialog";
import { ProblemDialog } from "../problemDialog/ProblemDialog";
import { ProgressDialog } from "../react_components/IndependentProgressDialog";
import { App } from "../app/App";

// this is a bummer... haven't figured out how to do a lookup just from the string... have to have this map
const knownComponents = {
    TeamCollectionSettingsPanel: TeamCollectionSettingsPanel,
    TeamCollectionDialog: TeamCollectionDialog,
    AutoUpdateSoftwareDialog: AutoUpdateSoftwareDialog,
    ProblemDialog: ProblemDialog,
    ProgressDialog: ProgressDialog,
    CollectionsTabPane: CollectionTabPane,
    App: App
};

// This is called from an html file created in the c# ReactControl class.
// export function wireUpReact  (
//     root: HTMLElement,
//     reactComponentName: string
// )  {
//     ReactDOM.render(
//         React.createElement(knownComponents[reactComponentName], {}, null),
//         root
//     );
// };

export function wire2(root: HTMLElement, reactComponentName: string) {
    ReactDOM.render(
        React.createElement(knownComponents[reactComponentName], {}, null),
        root
    );
}
export function wireUpReact(root: HTMLElement, reactComponentName: string) {
    ReactDOM.render(
        React.createElement(knownComponents[reactComponentName], {}, null),
        root
    );
}
