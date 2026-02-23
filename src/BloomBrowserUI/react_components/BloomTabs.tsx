import { css, SerializedStyles } from "@emotion/react";
import * as React from "react";
import { TabsProps, Tabs } from "react-tabs";
interface IProps extends TabsProps {
    color: string; // text, borders
    // text and bottom border for selected tab
    selectedColor: string;
    labelBackgroundColor: string;
}

// A wrapper around the react-tabs Tab element that applies some standard CSS we want in Bloom,
// and inserts some of the configurable elements in the proper places. This is a work-in-progress;
// as we identify more common behavior we want or more things we often want to configure we can
// improve it.
export const BloomTabs: React.FunctionComponent<IProps> = (props) => {
    const { color, selectedColor, labelBackgroundColor, ...tabsProps } = props;
    return (
        <Tabs
            {...tabsProps}
            css={css`
                height: 100%; // by default, we choose to make this greedy and fill up parent (note: flex-grow:1 doesn't work & isn't needed)
                display: flex;
                flex-direction: column;

                .react-tabs__tab.react-tabs__tab {
                    background-color: ${labelBackgroundColor};
                    color: ${color};
                    text-transform: uppercase;
                    display: inline-block;
                }
                .react-tabs__tab--selected {
                    color: ${selectedColor} !important;
                    font-weight: bold;
                    border-color: transparent;
                    border-bottom: 2px solid ${selectedColor};
                }
                .react-tabs__tab-list {
                    border: none;
                    list-style: none;
                    padding: 0;
                    margin-bottom: 5px; // else the little selector slams into the content and looks bad
                }
                .react-tabs__tab-panel {
                    display: none;
                    overflow-y: auto; // make these contents scroll if needed
                    height: 100%; // note, the child should normally also set height:100% to fill this up.
                }
                .react-tabs__tab-panel--selected {
                    display: block;
                }
            `}
        >
            {props.children}
        </Tabs>
    );
};
