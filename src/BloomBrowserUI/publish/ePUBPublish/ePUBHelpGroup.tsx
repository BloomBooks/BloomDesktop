import Link from "@mui/material/Link";
import { HelpGroup } from "../commonPublish/PublishScreenBaseComponents";
import HelpLink from "../../react_components/helpLink";
import { Div } from "../../react_components/l10nComponents";

export const EPUBHelpGroup = () => (
    <HelpGroup>
        <HelpLink
            l10nKey="PublishTab.Epub.Help.AboutEpubs"
            helpId="Concepts/EPUB.htm"
        >
            About ePUBs
        </HelpLink>
        <Link
            href="https://docs.bloomlibrary.org/ePUB-notes/"
            underline="hover"
        >
            <Div l10nKey="PublishTab.Epub.AboutReaders">
                About ePUB Readers & Bloom
            </Div>
        </Link>
        <HelpLink
            l10nKey="PublishTab.TasksOverview"
            helpId="Tasks/Publish_tasks/Publish_tasks_overview.htm"
        >
            Publish tab tasks overview
        </HelpLink>
    </HelpGroup>
);
