import * as React from "react";
import { ProgressBox } from "./progressBox";

export default {
    title: "Progress/Progress Box",
};

export const RawProgressBoxWithPreloadedLog = () => {
    return React.createElement(() => {
        return (
            <ProgressBox
                preloadedProgressEvents={[
                    {
                        id: "message",
                        clientContext: "unused",
                        message: "This is a preloaded log message",
                        progressKind: "Progress",
                    },
                    {
                        id: "message",
                        clientContext: "unused",
                        message: "This is a message about an error in the past",
                        progressKind: "Error",
                    },
                ]}
            />
        );
    });
};

RawProgressBoxWithPreloadedLog.story = {
    name: "Raw ProgressBox with preloaded log",
};
