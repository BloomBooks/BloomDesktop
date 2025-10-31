import type { IBloomComponentConfig } from "../../component-tester/componentTypes";
import type { BookInfoForLinks, Link } from "../BookLinkTypes";

export interface IBookGridSetupProps {
    sourceBooks: BookInfoForLinks[];
    links: Link[];
    onLinksChanged: ((links: Link[]) => void) | string;
}

const config: IBloomComponentConfig<IBookGridSetupProps> = {
    defaultProps: {
        sourceBooks: [
            {
                id: "book1",
                title: "The Moon Book",
                folderName: "The Moon Book",
                thumbnail:
                    "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
            },
            {
                id: "book2",
                title: "Counting Fun",
                folderName: "Counting Fun",
                thumbnail:
                    "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
            },
            {
                id: "book3",
                title: "Animal Friends",
                folderName: "Animal Friends",
                thumbnail:
                    "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
            },
        ],
        links: [],
        onLinksChanged: "testapi/bookGridSetup/linksChanged",
    },
    modulePath: "../bookLinkSetup/BookGridSetup",
    exportName: "BookGridSetup",
};

export default config;
