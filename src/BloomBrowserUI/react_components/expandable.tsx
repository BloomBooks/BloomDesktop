import * as React from "react";
import { Label } from "./l10n";
import { ILocalizationProps, LocalizableElement } from "./l10n";
import "./expandable.less";

// Expandable implements an area with a heading (e.g., Advanced in Sign Language tool)
// and next to it an arrow which can be clicked to display further content.
// The arrow rotates and gets filled in (per Microsoft guidelines, according to BL-6664)
// when expanded.
// A brief animation shows the control growing in height as it opens. This is achieved
// by a transition from height: 0 when closed to a height specified as the
// expandedHeight property when instantiating the component. The text of the heading
// is also provided as a property. All the Label properties may be used to
// internationalize it; it functions as the body of the label.
// Whatever you set up as the children of the Expandable is the material that will
// be shown when it is expanded. Since overflow is hidden to support the animation,
// it needs to fit within the expandedHeight.

export interface IComponentState {
    expanded: boolean;
}

interface IExpandableProps extends ILocalizationProps {
    headingText: string;
    expandedHeight: string;
    className?: string;
    expandInitially?: boolean;
}

export class Expandable extends React.Component<
    IExpandableProps,
    IComponentState
> {
    public readonly state: IComponentState = {
        expanded: false
    };

    constructor(props: IExpandableProps) {
        super(props);
    }

    public componentDidMount() {
        if (this.props.expandInitially) {
            this.setState({ expanded: true });
        }
    }

    public render() {
        return (
            <div className={"expandable " + (this.props.className || "")}>
                <div className="wrapper" onClick={() => this.toggleExpanded()}>
                    {/* hollow triangle pointing right. When box is expanded,
                        it transitions to rotated and invisible. */}
                    <div
                        className={
                            "triangle " +
                            (this.state.expanded ? "rotate" : "show")
                        }
                    >
                        &#x25b7;
                    </div>
                    {/* filled in triangle pointing right, initially hidden. When box is expanded,
                        it transitions to rotated and visible. */}
                    <div
                        className={
                            "triangle " +
                            (this.state.expanded ? "show rotate" : "")
                        }
                    >
                        &#x25b6;
                    </div>
                </div>
                <Label {...this.props} onClick={() => this.toggleExpanded()}>
                    {this.props.headingText}
                </Label>
                <div
                    className="contentWrap"
                    style={{
                        height: this.state.expanded
                            ? this.props.expandedHeight
                            : "0"
                    }}
                >
                    {this.props.children}
                </div>
            </div>
        );
    }

    private toggleExpanded() {
        this.setState({ expanded: !this.state.expanded });
    }
}
