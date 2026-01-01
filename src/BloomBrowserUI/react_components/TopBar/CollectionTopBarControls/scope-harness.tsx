// This file is used by `yarn scope` to open the component in a browser. It does so in a way that works with skills/scope/skill.md.

import * as React from "react";
import { css } from "@emotion/react";
import { CollectionTopBarControls } from "./CollectionTopBarControls";
import { mockReplies } from "../../../utils/bloomApi";

type CollectionTopBarHarnessProps = {
    status: string;
};

const CollectionTopBarHarness: React.FC<CollectionTopBarHarnessProps> = (
    props,
) => {
    // Keep CollectionTopBarControls functional without a Bloom backend by
    // answering bloomApi.get("teamCollection/tcStatus") from mockReplies.
    React.useEffect(() => {
        const table = mockReplies as unknown as Record<string, unknown>;
        table["teamCollection/tcStatus"] = { data: props.status };
    }, [props.status]);

    return (
        <div
            css={css`
                font-family: sans-serif;
                padding: 16px;
                background: #f5f5f5;
                height: 100vh;
                box-sizing: border-box;
            `}
        >
            <div
                css={css`
                    margin-bottom: 10px;
                    color: #333;
                    font-size: 13px;
                `}
            >
                teamCollection/tcStatus: {props.status}
            </div>
            <div
                css={css`
                    background: white;
                    padding: 8px;
                    border-radius: 6px;
                    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.15);
                `}
            >
                <CollectionTopBarControls />
            </div>
        </div>
    );
};

export const nominal: React.FC = () => {
    return <CollectionTopBarHarness status="Nominal" />;
};

export const newStuff: React.FC = () => {
    return <CollectionTopBarHarness status="NewStuff" />;
};

export const error: React.FC = () => {
    return <CollectionTopBarHarness status="Error" />;
};

export const disconnected: React.FC = () => {
    return <CollectionTopBarHarness status="Disconnected" />;
};

export const none: React.FC = () => {
    return <CollectionTopBarHarness status="None" />;
};

export default nominal;
