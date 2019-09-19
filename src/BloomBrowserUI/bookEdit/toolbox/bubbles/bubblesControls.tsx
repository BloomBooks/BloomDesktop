import * as React from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import "./Bubbles.less";
import { getPageFrameExports } from "../../js/bloomFrames";
import { TextOverPictureManager } from "../../js/textOverPicture";

interface IComponentState {}

// These classes support the Bubbles toolbox, which allows control of cartoon bubbles added to images.
export class BubblesToolControls extends React.Component<{}, IComponentState> {
    constructor(props: Readonly<{}>) {
        super(props);
    }

    public render() {
        return <div>bubbles tool</div>;
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

    private bubbleManager(): TextOverPictureManager {
        return getPageFrameExports().getTheOneBubbleManager();
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
        this.bubbleManager().turnOnBubbleEditing();
        //firstImage.classList.add("bloom-hideImageButtons");
    }

    public detachFromPage() {
        this.bubbleManager().turnOffBubbleEditing();
        // const firstImage = this.getFirstImage();
        // if (!firstImage) {
        //     return;
        // }
        // firstImage.classList.remove("bloom-hideImageButtons");
    }
}
