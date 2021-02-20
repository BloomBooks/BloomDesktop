import React = require("react");
import * as ReactDOM from "react-dom";
import { TeamCollectionSettingsPanel } from "../teamCollection/TeamCollectionSettingsPanel";

// this is a bummer... haven't figured out how to do a lookup just from the string... have to have this map
const knownComponents = {
    TeamCollectionSettingsPanel: TeamCollectionSettingsPanel
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
