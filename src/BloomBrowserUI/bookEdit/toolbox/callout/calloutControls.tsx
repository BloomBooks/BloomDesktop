import * as React from "react";
import { useState, useEffect } from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import * as ReactDOM from "react-dom";
import "./Callout.less";
import { getPageFrameExports } from "../../js/bloomFrames";
import { TextOverPictureManager } from "../../js/textOverPicture";
import { RadioGroup } from "../../../react_components/RadioGroup";
import { BubbleSpec } from "comical-js//bubbleSpec";
import { ToolBottomHelpLink } from "../../../react_components/helpLink";
import FormControl from "@material-ui/core/FormControl";
import Select from "@material-ui/core/Select";
import { MenuItem } from "@material-ui/core";
import { values } from "mobx";
import { Div } from "../../../react_components/l10nComponents";
import { createStyles, makeStyles, Theme } from "@material-ui/core/styles"; // TODO: Am I really needed?
import InputLabel from "@material-ui/core/InputLabel";
import FormHelperText from "@material-ui/core/FormHelperText";

interface ICalloutToolProps {
    style: string;
    bubbleActive: boolean;
}

const CalloutToolControls: React.FunctionComponent<ICalloutToolProps> = (
    props: ICalloutToolProps
) => {
    const [style, setStyle] = useState(props.style);
    const [bubbleActive, setBubbleActive] = useState(props.bubbleActive);

    // TODO: Where should i move?
    const bubbleSpecInitialization = () => {
        const bubbleManager = CalloutTool.bubbleManager();
        if (!bubbleManager) {
            // probably the toolbox just finished loading before the page.
            // No clean way to fix this
            return;
        }

        bubbleManager.turnOnBubbleEditing();

        const bubbleSpec = bubbleManager.getSelectedItemBubbleSpec();

        bubbleManager.requestBubbleChangeNotification(
            (bubble: BubbleSpec | undefined) => {
                // TODO: Ugh, this is a circular dependency-like scenario :(
                //setActiveBubbleSpec(bubble);

                if (bubble) {
                    setStyle(bubble.style);
                    setBubbleActive(true);
                }
            }
        );
        return bubbleSpec;
    };

    const [activeBubbleSpec, setActiveBubbleSpec] = useState(
        () => {
            return bubbleSpecInitialization();
        } // The function will only be evaluated the first time.
    );
    useEffect(() => {
        if (activeBubbleSpec) {
            setStyle(activeBubbleSpec.style);
            setBubbleActive(true);
        }

        // Return value is the Cleanup function
        return () => {
            const bubbleManager = CalloutTool.bubbleManager();
            if (bubbleManager) {
                bubbleManager.turnOffBubbleEditing();
                bubbleManager.detachBubbleChangeNotification();
            }
        };
    }, [activeBubbleSpec]);

    const handleStyleChanged = event => {
        const newStyle = event.target.value;

        // Update the toolbox controls
        setStyle(newStyle);

        // Update the Comical canvas on the page frame
        CalloutTool.bubbleManager().updateSelectedItemBubbleSpec({
            style: newStyle
        });
    };

    return (
        <div>
            <div id={"calloutControlShapeChooserRegion"}>
                Drag one of these on top of an image: TODO: Implement dragger
            </div>
            <div
                id={"calloutControlOptionsRegion"}
                className={bubbleActive ? "" : "disabled"}
            >
                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.ControlsAvailable">
                    The selected item has these controls:
                </Div>
                <br />
                <Div l10nKey="EditTab.Toolbox.CalloutTool.Options.Style">
                    Style
                </Div>
                <form autoComplete="off">
                    <FormControl>
                        {/* TODO: How to localize these? */}
                        <InputLabel htmlFor="callout-style-dropdown">
                            Style
                        </InputLabel>
                        <Select
                            value={style}
                            onChange={event => {
                                handleStyleChanged(event);
                            }}
                            className="calloutOptionDropdown"
                            inputProps={{
                                name: "style",
                                id: "callout-style-dropdown"
                            }}
                        >
                            <MenuItem value="caption">Caption (TODO)</MenuItem>
                            <MenuItem value="shout">Exclamation</MenuItem>
                            <MenuItem value="none">Just Text</MenuItem>
                            <MenuItem value="speech">Speech</MenuItem>
                            <MenuItem value="thought">Thought (TODO)</MenuItem>
                        </Select>
                    </FormControl>
                    <FormControl>
                        <InputLabel htmlFor="callout-textColor-dropdown">
                            Text Color
                        </InputLabel>
                        <Select
                            value={"white"}
                            className="calloutOptionDropdown"
                            inputProps={{
                                name: "textColor",
                                id: "callout-textColor-dropdown"
                            }}
                        >
                            <MenuItem value="white">White</MenuItem>
                            <MenuItem value="black">Black</MenuItem>
                        </Select>
                    </FormControl>
                </form>
            </div>
            <div id={"calloutControlFooterRegion"}>
                {/* TODO: Update the help link */}
                <ToolBottomHelpLink helpId="Tasks/Edit_tasks/Impairment_Visualizer/Impairment_Visualizer_overview.htm" />
            </div>
        </div>
    );
};
export default CalloutToolControls;

export class CalloutTool extends ToolboxToolReactAdaptor {
    //private reactControls: CalloutToolControls;
    private componentProps: ICalloutToolProps;

    public constructor() {
        super();

        // Just a default value
        this.componentProps = {
            style: "none",
            bubbleActive: false
        };
    }

    public makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "CalloutBody");

        //ReactDOM.render(<CalloutToolControls {...this.componentProps} />, root);
        ReactDOM.render(
            <CalloutToolControls
                style={this.componentProps.style}
                bubbleActive={this.componentProps.bubbleActive}
            />,
            root
        );
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
        console.log("New page ready");
        CalloutTool.newPageReadyStaticHelper();

        // Or should it attempt to force re-render?
        // What if the bubbleSpec changes?

        // const bubbleManager = CalloutTool.bubbleManager();
        // if (!bubbleManager) {
        //     // probably the toolbox just finished loading before the page.
        //     // No clean way to fix this
        //     window.setTimeout(() => this.newPageReady(), 100);
        //     return;
        // }
        // bubbleManager.turnOnBubbleEditing();
        // const bubbleSpec = bubbleManager.getSelectedItemBubbleSpec();
        // if (bubbleSpec) {
        //     this.componentProps.bubbleActive = true;
        //     this.componentProps.style = bubbleSpec.style;
        // }
        //     <CalloutToolControls {...props} />;
        // }
        // if (this.reactControls) {
        //     this.reactControls.updateBubbleState(
        //         bubbleManager.getSelectedItemBubbleSpec()
        //     );
        // }
        // bubbleManager.requestBubbleChangeNotification(bubble => {
        //     if (this.reactControls) {
        //         this.reactControls.updateBubbleState(bubble);
        //     }
        // });
    }

    public static newPageReadyStaticHelper() {
        const bubbleManager = CalloutTool.bubbleManager();
        if (!bubbleManager) {
            // probably the toolbox just finished loading before the page.
            // No clean way to fix this
            window.setTimeout(
                () => CalloutTool.newPageReadyStaticHelper(),
                100
            );
            return;
        }
        bubbleManager.turnOnBubbleEditing();
    }

    public detachFromPage() {
        CalloutTool.bubbleManager().turnOffBubbleEditing();
    }

    public static bubbleManager(): TextOverPictureManager {
        return getPageFrameExports().getTheOneBubbleManager();
    }
}
