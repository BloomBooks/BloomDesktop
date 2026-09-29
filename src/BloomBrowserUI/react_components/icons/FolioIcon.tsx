import { css } from "@emotion/react";
import * as React from "react";
import folioSvg from "../../images/folio.svg?raw";

// The folio icon (images/folio.svg: three books tied together with a bow). It draws in
// currentColor, so it takes the color given here.
export const FolioIcon: React.FunctionComponent<{
    className?: string;
    color: string;
}> = (props) => {
    return (
        <span
            className={props.className}
            css={css`
                display: inline-flex;
                color: ${props.color};
                svg {
                    width: 100%;
                    height: 100%;
                }
            `}
            dangerouslySetInnerHTML={{ __html: folioSvg }}
        />
    );
};
