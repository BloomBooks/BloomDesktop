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
            l10nKey="PublishTab.Epub.Accessibility"
            helpId="Tasks/Publish_tasks/Accessibility.htm"
        >
            Accessibility
        </HelpLink>
        <HelpLink
            l10nKey="PublishTab.Epub.Help.EReaders"
            helpId="Concepts/Epub_Readers.htm"
        >
            Compatible ePUB Readers
        </HelpLink>
        <HelpLink
            l10nKey="PublishTab.Epub.Help.Publishing"
            helpId="Tasks/Publish_tasks/Digital_publishing_options.htm"
        >
            Getting ePUBs onto a device
        </HelpLink>
    </HelpGroup>
);
