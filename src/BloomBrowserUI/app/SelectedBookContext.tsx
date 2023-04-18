import React = require("react");
import {
    DefaultSelectedBookInfo,
    ISelectedBookInfo,
    useMonitorBookSelection
} from "./selectedBook";

export const SelectedBookContext = React.createContext<ISelectedBookInfo>(
    DefaultSelectedBookInfo
);

export const SelectedBookProvider: React.FunctionComponent = props => {
    const selectedBookInfo = useMonitorBookSelection();
    return <SelectedBookContext.Provider value={selectedBookInfo} {...props} />;
};
