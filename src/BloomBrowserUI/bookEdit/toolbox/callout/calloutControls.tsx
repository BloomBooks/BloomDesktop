import * as React from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import "./Callout.less";
import { getPageFrameExports } from "../../js/bloomFrames";
import { TextOverPictureManager } from "../../js/textOverPicture";
import { RadioGroup } from "../../../react_components/RadioGroup";
import { BubbleSpec } from "comical-js//bubbleSpec";

interface IComponentState {
    style: string;
    bubbleActive: boolean;
}

// These classes support the Callouts toolbox, which allows control of callouts (e.g. cartoon bubbles) added to images.
export class CalloutToolControls extends React.Component<{}, IComponentState> {
    constructor(props: Readonly<{}>) {
        super(props);
    }

    public readonly state: IComponentState = {
        style: "speech",
        bubbleActive: true
    };

    public updateBubbleState(bubble: BubbleSpec | undefined) {
        this.setState({
            style: bubble ? bubble.style : "none",
            bubbleActive: !!bubble
        });
    }

    public render() {
        return (
            <div className={this.state.bubbleActive ? "" : "disabled"}>
                <RadioGroup
                    value={this.state.style}
                    onChange={s => this.setBubbleStyle(s)}
                    choices={{
                        none: "No bubble",
                        speech: "An oval bubble",
                        shout: "A jagged bubble for shouting"
                    }}
                />
            </div>
        );
    }

    public static bubbleManager(): TextOverPictureManager {
        return getPageFrameExports().getTheOneBubbleManager();
    }

    private setBubbleStyle(s: string): void {
        this.setState({ style: s });
        CalloutToolControls.bubbleManager().updateSelectedItemBubbleSpec({
            style: s
        });
    }

    public static setup(root): CalloutToolControls {
        return (ReactDOM.render(
            <CalloutToolControls />,
            root
        ) as unknown) as CalloutToolControls;
    }
}

export class CalloutTool extends ToolboxToolReactAdaptor {
    private reactControls: CalloutToolControls;

    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "CalloutBody");
        this.reactControls = CalloutToolControls.setup(root);
        return root as HTMLDivElement;
    }

    public id(): string {
        return "callout";
    }

    public isExperimental(): boolean {
        return true;
    }

    public toolRequiresEnterprise(): boolean {
        return false; // review
    }

    public beginRestoreSettings(settings: string): JQueryPromise<void> {
        // Nothing to do, so return an already-resolved promise.
        const result = $.Deferred<void>();
        result.resolve();
        return result;
    }

    public newPageReady() {
        const bubbleManager = CalloutToolControls.bubbleManager();
        if (!bubbleManager) {
            // probably the toolbox just finished loading before the page.
            // No clean way to fix this
            window.setTimeout(() => this.newPageReady(), 100);
            return;
        }
        bubbleManager.turnOnBubbleEditing();
        if (this.reactControls) {
            this.reactControls.updateBubbleState(
                bubbleManager.getSelectedItemBubbleSpec()
            );
        }
        bubbleManager.requestBubbleChangeNotification(bubble => {
            if (this.reactControls) {
                this.reactControls.updateBubbleState(bubble);
            }
        });
    }

    public detachFromPage() {
        CalloutToolControls.bubbleManager().turnOffBubbleEditing();
    }
}
