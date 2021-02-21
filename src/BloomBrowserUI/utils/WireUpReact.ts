import React = require("react");
import * as ReactDOM from "react-dom";
import { BookListPane } from "../collectionTab/BookListPane";
import { TeamCollectionSettingsPanel } from "../teamCollection/TeamCollectionSettingsPanel";
import { TeamCollectionDialog } from "../teamCollection/TeamCollectionDialog";
import { AutoUpdateSoftwareDialog } from "../react_components/AutoUpdateSoftwareDialog";
import { ProblemDialog } from "../problemDialog/ProblemDialog";
import { ProgressDialog } from "../react_components/IndependentProgressDialog";

// this is a bummer... haven't figured out how to do a lookup just from the string... have to have this map
const knownComponents = {
    TeamCollectionSettingsPanel: TeamCollectionSettingsPanel,
    TeamCollectionDialog: TeamCollectionDialog,
    AutoUpdateSoftwareDialog: AutoUpdateSoftwareDialog,
    ProblemDialog: ProblemDialog,
    ProgressDialog: ProgressDialog,
    BookListPane: BookListPane
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
