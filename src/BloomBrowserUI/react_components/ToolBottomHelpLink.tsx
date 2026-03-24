import * as React from "react";
import HelpLink from "./helpLink";

// A common help link is just called "help" and that has a high zindex so that it is usable even when the tool is disabled by an overlay
export class ToolBottomHelpLink extends React.Component<{ helpId: string }> {
    public render() {
        return (
            <HelpLink
                style={{ zIndex: 100 }}
                l10nKey="Common.Help"
                helpId={this.props.helpId}
            >
                Help
            </HelpLink>
        );
    }
}
