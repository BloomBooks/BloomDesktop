import React = require("react");

import {
    BloomDialog,
    DialogMiddle,
    DialogBottomButtons,
    DialogTitle
} from "../../react_components/BloomDialog/BloomDialog";
import { useSetupBloomDialog } from "../../react_components/BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogOkButton
} from "../../react_components/BloomDialog/commonDialogComponents";
import { DialogResult } from "../../react_components/color-picking/colorPickerDialog";
import {
    post,
    postJson,
    postString,
    useApiObject,
    useApiStringState
} from "../../utils/bloomApi";
import { ShowEditViewDialog } from "../editViewFrame";
import { useL10n } from "../../react_components/l10nHooks";
import LinkGridSetup from "./LinkGridSetup";
import { css } from "@emotion/react";
import {
    BookInfoForLinks,
    CollectionInfoForLinkChoosing,
    Link
} from "./BookLinkTypes";

export const LinkGridSetupDialog: React.FunctionComponent<{}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog({
        initiallyOpen: true,
        dialogFrameProvidedExternally: false
    });

    const [settingsString] = useApiStringState(
        "book/settings",
        "{}",
        () => propsForBloomDialog.open
    );

    const [settings, setSettings] = React.useState<object | undefined>(
        undefined
    );

    const [settingsToReturnLater, setSettingsToReturnLater] = React.useState(
        ""
    );

    React.useEffect(() => {
        if (settingsString === "{}") {
            return; // leave settings as undefined
        }
        if (typeof settingsString === "string") {
            setSettings(JSON.parse(settingsString));
        } else {
            setSettings(settingsString);
        }
    }, [settingsString]);
    const dialogTitle = useL10n("Link Grid Setup", "LinkGridSetup.Title");

    function saveSettingsAndCloseDialog() {
        closeDialog();
    }
    const [currentCollection, setCurrentCollection] = React.useState(0);
    const initialLinks: Link[] = collections[0].books
        .slice(0, 4)
        .map(book => ({ book }));
    const [selectedLinks, setSelectedLinks] = React.useState<Link[]>(
        initialLinks
    );

    const generateThumbnail = (bookId: string, pageNumber: number) =>
        `https://picsum.photos/seed/${bookId}-page${pageNumber}/160/90?grayscale`;

    return (
        <BloomDialog
            {...propsForBloomDialog}
            onClose={closeDialog}
            onCancel={() => {
                closeDialog();
            }}
            draggable={false}
            maxWidth={false}
        >
            <DialogTitle title={dialogTitle} />
            <DialogMiddle>
                {" "}
                <LinkGridSetup
                    sourceBooks={collections[currentCollection].books}
                    collectionNames={collections.map(c => c.name)}
                    currentCollection={currentCollection}
                    onCollectionChange={setCurrentCollection}
                    onLinksChanged={setSelectedLinks}
                    links={selectedLinks}
                    thumbnailGenerator={generateThumbnail}
                />
                <LinkDisplay links={selectedLinks} />
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    default={true}
                    onClick={saveSettingsAndCloseDialog}
                />
                <DialogCancelButton />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

export function showLinkGridSetupsDialog() {
    ShowEditViewDialog(<LinkGridSetupDialog />);
}

const loremWords = [
    "Lorem",
    "Ipsum",
    "Dolor",
    "Sit",
    "Amet",
    "Consectetur",
    "Adipiscing",
    "Elit",
    "Sed",
    "Eiusmod",
    "Tempor",
    "Incididunt",
    "Labore",
    "Magna"
];

const generateLoremTitle = () => {
    const wordCount = 1 + Math.floor(Math.random() * 5); // 1-5 words
    return Array.from(
        { length: wordCount },
        () => loremWords[Math.floor(Math.random() * loremWords.length)]
    ).join(" ");
};

const createSampleBooks = (count: number, grade: number): BookInfoForLinks[] =>
    Array.from({ length: count }, (_, i) => ({
        id: `grade${grade}-book-${i + 1}`,
        title: generateLoremTitle(),
        thumbnail: `https://picsum.photos/seed/${grade * 100 + i}/160/90`,
        pageLength: 5 + i * 7 // Start at 5 pages and increment by 3 for each book
    }));

const collections: CollectionInfoForLinkChoosing[] = [
    { name: "Grade 1", books: createSampleBooks(20, 1) },
    { name: "Grade 2", books: createSampleBooks(20, 2) },
    { name: "Grade 3", books: createSampleBooks(20, 3) }
];

const LinkDisplay: React.FC<{ links: Link[] }> = ({ links }) => (
    <div
        css={css`
            background: white;
            padding: 16px;
            border-radius: 4px;
            margin-top: 16px;
            box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
            font-family: sans-serif;
        `}
    >
        <h3
            css={css`
                margin-top: 0;
                margin-bottom: 8px;
            `}
        >
            Selected Links
        </h3>
        {links.map((link, index) => (
            <div key={index}>
                Book ID: {link.book.id}
                {link.page && `, Page: ${link.page.pageId}`}
            </div>
        ))}
    </div>
);
