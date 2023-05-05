/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import { Link } from "@mui/material";
import { DialogControlGroup } from "./BloomDialog/commonDialogComponents";

interface IBookInfoCardProps {
    title: string;
    bookUrl?: string;
    languages: string[];
    thumbnailUrl?: string;
    originalUpload?: string;
    lastUpdated?: string;
}

export const BookInfoCard: React.FunctionComponent<IBookInfoCardProps> = props => {
    const languageList = props.languages.map((languageName, index) => {
        return <div key={index}>{languageName}</div>;
    });

    const titleWithOptionalLink = (): JSX.Element => (
        <div>
            {props.bookUrl ? (
                <Link color="primary" underline="always" href={props.bookUrl}>
                    {props.title}
                </Link>
            ) : (
                props.title
            )}
        </div>
    );

    return (
        <div
            css={css`
                width: 300px;
                max-height: 150px;
                overflow-y: auto;
            `}
        >
            <DialogControlGroup>
                <div
                    css={css`
                        display: flex;
                        flex-direction: row;
                    `}
                >
                    {props.thumbnailUrl && (
                        <div
                            css={css`
                                margin-right: 8px;
                                display: flex;
                                flex-direction: column;
                                justify-content: flex-start;
                            `}
                        >
                            <img
                                css={css`
                                    width: 70px;
                                    min-height: 50px;
                                `}
                                src={props.thumbnailUrl}
                            />
                        </div>
                    )}
                    <div
                        css={css`
                            font-size: 11pt;
                        `}
                    >
                        {titleWithOptionalLink()}
                        {languageList}
                        {props.originalUpload && (
                            <div>Uploaded {props.originalUpload}</div>
                        )}
                        {props.lastUpdated && (
                            <div>Last updated on {props.lastUpdated}</div>
                        )}
                    </div>
                </div>
            </DialogControlGroup>
        </div>
    );
};
