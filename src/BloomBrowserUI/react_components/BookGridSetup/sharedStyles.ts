import { css } from "@emotion/react";

export const bookGridContainerStyles = css`
    display: flex;
    flex-wrap: wrap;
    gap: 20px;
    align-content: flex-start;
    border-radius: 4px;
    border: solid 1px gray;
    padding: 20px;
    height: 100%;
    overflow-y: auto;
    overflow-x: hidden;
    box-sizing: border-box;
`;
