import React = require("react");
import { ISelectedBookInfo, useMonitorBookSelection } from "./selectedBook";

export const SelectedBookContext = React.createContext<ISelectedBookInfo>(
    {} as ISelectedBookInfo
);

export const SelectedBookProvider: React.FunctionComponent = props => {
    const selectedBookInfo = useMonitorBookSelection();
    return <SelectedBookContext.Provider value={selectedBookInfo} {...props} />;
};
