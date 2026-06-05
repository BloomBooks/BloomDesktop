import ToolboxToolReactAdaptor from "../../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import { DecodableReaderToolControls } from "./DecodableReaderToolControls";

export class DecodableReaderTool extends ToolboxToolReactAdaptor {
    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        //root.setAttribute("class", "CanvasBody");

        ReactDOM.render(<DecodableReaderToolControls />, root);
        return root as HTMLDivElement;
    }
    public id(): string {
        //return "decodableReader";
        return "decodableReaderNew";
    }
}
