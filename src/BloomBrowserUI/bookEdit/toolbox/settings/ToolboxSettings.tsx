import { renderRoot } from "../../../utils/reactRender";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import { ToolboxSettingsControls } from "./ToolboxSettingsControls";

export class ToolboxSettings extends ToolboxToolReactAdaptor {
    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");

        renderRoot(<ToolboxSettingsControls />, root);
        return root as HTMLDivElement;
    }
    public id(): string {
        return "settings";
    }
}
