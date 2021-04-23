import * as React from "react";
import { storiesOf } from "@storybook/react";
import { PerformanceLogPage } from "./PerformanceLogPage";

/* this is causing lots of errors when Bloom isn't running, possibly interfering with other stories.

storiesOf("PerformanceLogPage", module).add("Some points", () => (
    <div>
        <PerformanceLogPage
            webSocketContext="mock_performance"
            initialPoints={[
                {
                    privateBytes: 1059736,
                    action: "Select Page",
                    details: "Page 13",
                    duration: 1
                },
                { privateBytes: 1152084, duration: 1 },
                { privateBytes: 1326368, duration: 2 },
                { privateBytes: 1755616, duration: 2.2 },
                { privateBytes: 2096360, duration: 1.2 }
            ]}
        />
    </div>
));
*/
