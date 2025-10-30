import { css } from "@emotion/react";

import * as React from "react";
import ToolboxToolReactAdaptor from "../toolboxToolReactAdaptor";
import { Div, Span } from "../../../react_components/l10nComponents";
import { get, postDataWithConfig } from "../../../utils/bloomApi";
import "./impairmentVisualizer.less";
import { RadioGroup } from "../../../react_components/RadioGroup";
import { deuteranopia, tritanopia, achromatopsia } from "color-blind";
import { ToolBottomHelpLink } from "../../../react_components/helpLink";
import { kImageContainerClass } from "../../js/bloomImages";
import { CanvasElementManager } from "../../js/CanvasElementManager";
import { ThemeProvider } from "@mui/material";
import { ApiCheckbox } from "../../../react_components/ApiCheckbox";
import { toolboxTheme } from "../../../bloomMaterialUITheme";

interface IState {
    kindOfColorBlindness: string;
}

// This react class implements the UI for the accessible images toolbox.
// Note: this file is included in toolboxBundle.js because webpack.config says to include all
// tsx files in bookEdit/toolbox.
// The toolbox is included in the list of tools because of the one line of immediately-executed code
// which  passes an instance of ImpairmentVisualizerAdaptor to ToolBox.registerTool();
export class ImpairmentVisualizerControls extends React.Component<
    unknown,
    IState
> {
    public readonly state: IState = {
        kindOfColorBlindness: "redGreen",
    };

    // This wants to be part of our state, passed as a prop to ApiBackedCheckbox.
    // But then we, and all the other clients of that class, have to be responsible
    // for interacting with the api to get and set that state. So, for the moment,
    // we just let the check box tell us what its value should be using onCheckChanged,
    // and use it to update the appearance of the page. Better solution wanted!
    private simulatingCataracts: boolean;
    private simulatingColorBlindness: boolean;

    public render() {
        const radioLabelElement = (
            label: string,
            l10nKey: string,
        ): JSX.Element => <Span l10nKey={l10nKey}>{label}</Span>;
        return (
            <ThemeProvider theme={toolboxTheme}>
                <div className="impairmentVisualizerBody">
                    <div className="impairmentVisualizerInnerWrapper">
                        <Div l10nKey="EditTab.Toolbox.ImpairmentVisualizer.Overview">
                            You can use these check boxes to have Bloom simulate
                            how your images would look with various visual
                            impairments.
                        </Div>
                        <ApiCheckbox
                            label="Cataracts"
                            l10nKey="EditTab.Toolbox.ImpairmentVisualizer.Cataracts"
                            apiEndpoint="accessibilityCheck/cataracts"
                            onChange={(simulate) =>
                                this.updateCataracts(simulate)
                            }
                            size="small"
                        />
                        <ApiCheckbox
                            label="Color Blindness"
                            l10nKey="EditTab.Toolbox.ImpairmentVisualizer.ColorBlindness"
                            apiEndpoint="accessibilityCheck/colorBlindness"
                            onChange={(simulate) =>
                                this.updateColorBlindnessCheck(simulate)
                            }
                            size="small"
                        />
                        <RadioGroup
                            onChange={(val) =>
                                this.updateColorBlindnessRadio(val)
                            }
                            value={this.state.kindOfColorBlindness}
                            choices={{
                                RedGreen: radioLabelElement(
                                    "Red-Green",
                                    "EditTab.Toolbox.ImpairmentVisualizer.RedGreen",
                                ),
                                BlueYellow: radioLabelElement(
                                    "Blue-Yellow",
                                    "EditTab.Toolbox.ImpairmentVisualizer.BlueYellow",
                                ),
                                Complete: radioLabelElement(
                                    "Complete",
                                    "EditTab.Toolbox.ImpairmentVisualizer.Complete",
                                ),
                            }}
                            radioSize="small"
                            css={css`
                                margin-left: 25px;
                                padding-top: 8px;
                            `}
                        ></RadioGroup>
                    </div>

                    <ToolBottomHelpLink helpId="Tasks/Edit_tasks/Impairment_Visualizer/Impairment_Visualizer_overview.htm" />
                </div>
            </ThemeProvider>
        );
    }

    private updateCataracts(simulate: boolean) {
        this.simulatingCataracts = simulate;
        this.updateSimulations(undefined);
    }

    private updateColorBlindnessCheck(simulate: boolean) {
        this.simulatingColorBlindness = simulate;
        this.updateSimulations(undefined);
    }

    private updateColorBlindnessRadio(mode: string) {
        postDataWithConfig("accessibilityCheck/kindOfColorBlindness", mode, {
            headers: { "Content-Type": "application/json" },
        });
        this.setState({ kindOfColorBlindness: mode });
        // componentDidUpdate will call updateSimulations when state is stable
    }

    public componentDidMount() {
        get("accessibilityCheck/kindOfColorBlindness", (result) => {
            this.setState({ kindOfColorBlindness: result.data });
        });
    }

    public componentDidUpdate(prevProps, prevState: IState) {
        this.updateSimulations(undefined);
    }

    // Make the state of the impairment simulations consistent with the current state of things.
    // Usually called with the argument undefined, in which case, it updates the simulations
    // for all images on the current page. When the caller knows that only one image is affected
    // (e.g., cropping is changing on that one image), passing the specific image that needs
    // updating makes things faster, since the other simulations need not be re-created.
    // The time taken to generate the simulations is significant, especially the color-blindness
    // ones, which are done a pixel at a time, so if they are being updated  frequently (like
    // during a drag), this optimization really helps make things less jerky.
    public updateSimulations(img: HTMLImageElement | undefined) {
        const page = ToolboxToolReactAdaptor.getPage();
        if (!page || !page.ownerDocument) return;
        const body = page.ownerDocument.body;
        if (this.simulatingCataracts) {
            body.classList.add("simulateCataracts");
        } else {
            body.classList.remove("simulateCataracts");
        }
        ImpairmentVisualizerControls.removeColorBlindnessMarkup(
            img ? img.parentElement! : page,
        );
        if (this.simulatingColorBlindness) {
            body.classList.add("simulateColorBlindness");
            // For now limit it to these images because the positioning depends
            // on the img being the first thing in its parent and the parent
            // being positioned, which we can't count on for other images.
            const containers =
                page.getElementsByClassName(kImageContainerClass);
            // img instanceof HTMLImageElement does not work here, possibly because img belongs to
            // a different iframe, which has its own HTMLImageElement prototype
            if (img) {
                this.makeColorBlindnessOverlay(img);
            } else {
                for (let i = 0; i < containers.length; i++) {
                    const immediateChildren = containers[i].children;
                    for (
                        let childIndex = 0;
                        childIndex < immediateChildren.length;
                        childIndex++
                    ) {
                        const child = immediateChildren[
                            childIndex
                        ] as HTMLElement;
                        if (!child || child.nodeName !== "IMG") continue;
                        // Don't make a overlay for a draghandle or other UI element.
                        if (child.classList.contains("bloom-ui")) continue;
                        this.makeColorBlindnessOverlay(
                            child as HTMLImageElement,
                        );
                    }
                }
            }
        } else {
            body.classList.remove("simulateColorBlindness");
        }
    }

    public static removeImpairmentVisualizerMarkup() {
        const page = ToolboxToolReactAdaptor.getPage();
        if (!page || !page.ownerDocument) return;
        ImpairmentVisualizerControls.removeColorBlindnessMarkup(page);
        const body = page.ownerDocument.body;
        body.classList.remove("simulateColorBlindness");
        body.classList.remove("simulateCataracts");
    }

    // Caller is responsible for guarding against a null page parameter.
    private static removeColorBlindnessMarkup(page: HTMLElement) {
        [].slice
            .call(page.getElementsByClassName("ui-cbOverlay"))
            .map((x) => x.parentElement.removeChild(x));
    }

    private componentToHex(c) {
        const hex = c.toString(16);
        return hex.length == 1 ? "0" + hex : hex;
    }

    private rgbToHex(r, g, b) {
        return (
            "#" +
            this.componentToHex(r) +
            this.componentToHex(g) +
            this.componentToHex(b)
        );
    }

    private makeColorBlindnessOverlay(img: HTMLImageElement) {
        if (
            !img.complete ||
            img.naturalWidth === undefined ||
            img.naturalWidth === 0 ||
            img.parentElement === null // paranoid
        ) {
            // The image isn't loaded, so we can't make a color-blind simulation of it.
            // We could add an event listener for "loaded", but then we have to worry about
            // removing it again...just waiting a bit is simpler.
            window.setTimeout(() => this.makeColorBlindnessOverlay(img), 100);
            return;
        }
        if (img.getAttribute("src") === "placeHolder.png") {
            // I don't think any purpose is served by visualizing what color blindness does to
            // a greyscale image that won't show in the real book, and it makes for more
            // updates to correctly handle when it shows and hides.
            return;
        }
        const page = ToolboxToolReactAdaptor.getPage();
        if (!page || !page.ownerDocument) return;
        const canvas = page.ownerDocument.createElement("canvas");
        const imageContainer = img.parentElement;
        const canvasElement = imageContainer.parentElement;
        // Make the canvas be the size the image is actually drawn.
        // Typically that means fewer pixels to calculate than doing the whole
        // image. To avoid distortion, we do have to make it the right shape.
        let canvasWidth = img.width;
        let canvasHeight = img.height;
        if (img.naturalWidth / img.naturalHeight < canvasWidth / canvasHeight) {
            // available space is too wide: make narrower
            canvasWidth = (canvasHeight * img.naturalWidth) / img.naturalHeight;
        } else {
            // available space may be too tall: make shorter
            canvasHeight = (canvasWidth * img.naturalHeight) / img.naturalWidth;
        }
        if (canvasElement) {
            canvasWidth = canvasElement.clientWidth;
            canvasHeight = canvasElement.clientHeight;
        }
        canvas.width = canvasWidth;
        canvas.height = canvasHeight;
        // Make the canvas fill the image-container, like the img.
        // This allows object-fit and object-position to put it where we want.
        canvas.style.position = "absolute";
        canvas.style.left = "0";
        canvas.style.top = "0";
        canvas.style.height = "100%";
        canvas.style.width = "100%";
        // And position it within the container the same as the img.
        if (img && img.ownerDocument && img.ownerDocument.defaultView) {
            img.style.objectPosition = img.ownerDocument.defaultView // the window that the img is in (not ours!)
                .getComputedStyle(img)
                .getPropertyValue("object-position");
        }
        canvas.classList.add("ui-cbOverlay"); // used to remove them
        const context = canvas.getContext("2d");
        if (!context) return; // paranoid
        const imgLeft = CanvasElementManager.pxToNumber(img.style.left ?? "0");
        const imgTop = CanvasElementManager.pxToNumber(img.style.top ?? "0");
        // This was determined pretty much by trial and error. The documentation of this function is very confusing.
        // Somehow, the first four arguments tell it what part of the image to draw, using natural dimensions. Thus,
        // the first four arguments tell it to draw the whole image (though this is larger than the canvas, if cropped).
        // The last four tell it where on the canvas to draw it and how to scale it, and these values seem to work,
        // though I cannot explain why.
        // I'm not sure what will happen if for some reason we can't get a natural size for the image.
        context.drawImage(
            img,
            0,
            0,
            img.naturalWidth,
            img.naturalHeight,
            imgLeft,
            imgTop,
            img.clientWidth,
            img.clientHeight,
        );
        // imgData is a byte array with 4 bytes for each pixel in RGBA order
        const imgData = context.getImageData(0, 0, canvas.width, canvas.height);
        const data = imgData.data;
        let cbAdapter = deuteranopia;
        if (this.state.kindOfColorBlindness == "BlueYellow") {
            cbAdapter = tritanopia;
        } else if (this.state.kindOfColorBlindness == "Complete") {
            cbAdapter = achromatopsia;
        }
        for (let ipixel = 0; ipixel < data.length / 4; ipixel++) {
            const r = data[ipixel * 4];
            const g = data[ipixel * 4 + 1];
            const b = data[ipixel * 4 + 2];
            const colorString = this.rgbToHex(r, g, b);
            const newRgb = cbAdapter(colorString, true);
            data[ipixel * 4] = newRgb.R;
            data[ipixel * 4 + 1] = newRgb.G;
            data[ipixel * 4 + 2] = newRgb.B;
            //data[ipixel * 4 + 3] = 255; // make the new image opaque.
        }
        context.putImageData(imgData, 0, 0);

        img.parentElement.appendChild(canvas);
    }
}

// This class implements the ITool interface through our adaptor's abstract methods by calling
// the appropriate ImpairmentVisualizerControls methods.
export class ImpairmentVisualizerAdaptor extends ToolboxToolReactAdaptor {
    // At first glance one would assume "| undefined" to be better here,
    // but the type of 'controlsElement' has to match the 'ref' attribute below
    // which has "| null".
    private controlsElement: ImpairmentVisualizerControls | null;

    public makeRootElement(): HTMLDivElement {
        return super.adaptReactElement(
            <ImpairmentVisualizerControls
                ref={(renderedElement) =>
                    (this.controlsElement = renderedElement)
                }
            />,
        );
    }
    public imageUpdated(img: HTMLImageElement | undefined): void {
        if (this.controlsElement) {
            this.controlsElement.updateSimulations(img);
        }
    }

    public id(): string {
        return "impairmentVisualizer";
    }

    public showTool() {
        if (!this.controlsElement) return;
        this.controlsElement.updateSimulations(undefined);
    }

    public newPageReady() {
        if (!this.controlsElement) return;
        this.controlsElement.updateSimulations(undefined);
    }

    public detachFromPage() {
        ImpairmentVisualizerControls.removeImpairmentVisualizerMarkup();
    }

    public isExperimental(): boolean {
        return false;
    }

    public toolRequiresEnterprise(): boolean {
        return false;
    }
}
