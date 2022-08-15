import { HelpGroup } from "../commonPublish/BasePublishScreen";
import HelpLink from "../../react_components/helpLink";
import React = require("react");

export const EPUBHelpGroup = () => (
    <HelpGroup>
        <HelpLink
            l10nKey="PublishTab.Epub.Help.AboutEpubs"
            helpId="Concepts/EPUB.htm"
        >
            About ePUBs
        </HelpLink>
        <HelpLink
            l10nKey="PublishTab.TasksOverview"
            helpId="Tasks/Publish_tasks/Publish_tasks_overview.htm"
        >
            Publish tab tasks overview
        </HelpLink>
    </HelpGroup>
);
