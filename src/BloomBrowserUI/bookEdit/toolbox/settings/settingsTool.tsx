import { renderRoot } from "../../../utils/reactRender";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import { SettingsToolControls } from "./SettingsToolControls";

// This class renders the SettingsToolControls React component
// for the toolbox. The settings are the menu of tools that
// appear when you press the "More..." button. A lot of the
// abstract functions from ToolboxToolReactAdaptor don't
// need to be implemented here, since there is no logic
// involving markup or attaching/detaching a tool that's
// needed here.
export class SettingsTool extends ToolboxToolReactAdaptor {
    // renders the SettingsToolControls component as a
    // div root element, to be displayed in the toolbox
    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");

        renderRoot(<SettingsToolControls />, root);
        return root as HTMLDivElement;
    }

    // returns the id for this "tool" so that it
    // can be properly bootstrapped
    public id(): string {
        return "settings";
    }
}
