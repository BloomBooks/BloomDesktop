import * as React from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import "./Bubbles.less";
import { getPageFrameExports } from "../../js/bloomFrames";
import { TextOverPictureManager } from "../../js/textOverPicture";
import { RadioGroup } from "../../../react_components/RadioGroup";
import { Bubble } from "bubble-edit//bubble";

interface IComponentState {
    style: string;
    bubbleActive: boolean;
}

// These classes support the Bubbles toolbox, which allows control of cartoon bubbles added to images.
export class BubblesToolControls extends React.Component<{}, IComponentState> {
    constructor(props: Readonly<{}>) {
        super(props);
    }

    public readonly state: IComponentState = {
        style: "speech",
        bubbleActive: true
    };

    public updateBubbleState(bubble: Bubble | undefined) {
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
        BubblesToolControls.bubbleManager().updateSelectedItemBubble({
            style: s
        });
    }

    public static setup(root): BubblesToolControls {
        return (ReactDOM.render(
            <BubblesToolControls />,
            root
        ) as unknown) as BubblesToolControls;
    }
}

export class BubblesTool extends ToolboxToolReactAdaptor {
    private reactControls: BubblesToolControls;

    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "BubblesBody");
        this.reactControls = BubblesToolControls.setup(root);
        return root as HTMLDivElement;
    }

    public id(): string {
        return "bubbles";
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
        const bubbleManager = BubblesToolControls.bubbleManager();
        if (!bubbleManager) {
            // probably the toolbox just finished loading before the page.
            // No clean way to fix this
            window.setTimeout(() => this.newPageReady(), 100);
            return;
        }
        bubbleManager.turnOnBubbleEditing();
        if (this.reactControls) {
            this.reactControls.updateBubbleState(
                bubbleManager.getSelectedItemBubble()
            );
        }
        bubbleManager.requestBubbleChangeNotification(bubble => {
            if (this.reactControls) {
                this.reactControls.updateBubbleState(bubble);
            }
        });
        //firstImage.classList.add("bloom-hideImageButtons");
    }

    public detachFromPage() {
        BubblesToolControls.bubbleManager().turnOffBubbleEditing();
        // const firstImage = this.getFirstImage();
        // if (!firstImage) {
        //     return;
        // }
        // firstImage.classList.remove("bloom-hideImageButtons");
    }
}
