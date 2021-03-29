import * as React from "react";
import { useState } from "react";
import { BloomApi } from "../utils/bloomApi";
import {
    useWebSocketListenerForOneMessage,
    useWebSocketListenerForOneObject
} from "../utils/WebSocketManager";
import "./PerformanceLogPage.less";
import { ScatterPlot } from "@nivo/scatterplot";
import ReactDOM = require("react-dom");
import { Button } from "@material-ui/core";
const filesize = require("filesize");

interface IMeasurement {
    action?: string;
    details?: string;
    privateBytes: number;
    duration: number;
}

export const PerformanceLogPage: React.FunctionComponent<{
    initialPoints?: IMeasurement[];
}> = props => {
    const [measurements, setMeasurements] = useState<IMeasurement[]>(
        props.initialPoints || []
    );

    const [applicationInfo] = BloomApi.useApiString(
        "performance/applicationInfo",
        ""
    );

    const [earlierMeasurements] = BloomApi.useApiObject<IMeasurement[]>(
        "performance/allMeasurements",
        []
    );
    React.useEffect(() => {
        setMeasurements(earlierMeasurements);
    }, earlierMeasurements);

    useWebSocketListenerForOneObject<IMeasurement>(
        "performance",
        "event",
        measurement => {
            setMeasurements(previous => {
                return [...previous, measurement];
            });
        }
    );
    //const websocket = new WebSocket("ws://127.0.0.1:8090");

    const allMeasurements = earlierMeasurements.concat(measurements);
    return (
        <div id="performanceLogControl">
            <h1>Bloom Performance Measurement</h1>
            <div>
                {applicationInfo}
                {/* <span style={{ marginLeft: "auto" }}>
                    {mostRecentMeasurement &&
                        `${mostRecentMeasurement.action} ${mostRecentMeasurement.details}`}
                </span> */}
            </div>

            <div className="combinedGraphs">
                <MemoryGraph measurements={allMeasurements} />
                <DurationGraph measurements={allMeasurements} />
            </div>
            <Button
                variant="contained"
                onClick={() => {
                    BloomApi.post("performance/showCsvFile");
                }}
            >
                Show CSV File
            </Button>
        </div>
    );
};

const MemoryGraph: React.FunctionComponent<{
    measurements: IMeasurement[];
}> = props => {
    const memoryLevels = props.measurements.map((m, index) => ({
        x: index,
        y: m.privateBytes / 1000000
    }));
    return (
        <div className="graph">
            <h2>Memory</h2>
            <ScatterPlot
                height={300}
                width={1000}
                margin={{ top: 10, right: 10, bottom: 30, left: 90 }}
                yScale={{ type: "linear", min: 0, max: "auto" }}
                yFormat={e =>
                    filesize(
                        (e as number) *
                        1000000 /* undo conversion to GB*/ *
                            1000 /* kb->bytes */
                    )
                }
                colors={{ scheme: "category10" }}
                axisTop={null}
                axisRight={null}
                axisBottom={{
                    orient: "bottom",
                    tickSize: 5,
                    tickPadding: 5,
                    tickValues: memoryLevels.length
                }}
                axisLeft={{
                    orient: "left",
                    tickSize: 5,
                    tickPadding: 5,
                    tickRotation: 0,
                    legend: "private bytes GB",
                    legendPosition: "middle",
                    legendOffset: -60
                }}
                tooltip={({ node }) => getTooltip(node, props.measurements)}
                legends={[
                    {
                        anchor: "bottom-right",
                        direction: "column",
                        justify: false,
                        translateX: 130,
                        translateY: 0,
                        itemWidth: 100,
                        itemHeight: 12,
                        itemsSpacing: 5,
                        itemDirection: "left-to-right",
                        symbolSize: 12,
                        symbolShape: "circle",
                        effects: [
                            {
                                on: "hover",
                                style: {
                                    itemOpacity: 1
                                }
                            }
                        ]
                    }
                ]}
                data={[
                    {
                        id: "",
                        data: memoryLevels
                    }
                ]}
            />
        </div>
    );
};
const DurationGraph: React.FunctionComponent<{
    measurements: IMeasurement[];
}> = props => {
    const durations = props.measurements.map((m, index) => ({
        x: index,
        y: m.duration
    }));
    return (
        <div className="graph">
            <h2>Time</h2>
            <ScatterPlot
                height={300}
                width={1000}
                margin={{ top: 10, right: 10, bottom: 30, left: 90 }}
                yScale={{ type: "linear", min: 0, max: "auto" }}
                yFormat={e => e + " seconds"}
                colors={{ scheme: "set1" }}
                axisTop={null}
                axisRight={null}
                axisBottom={{
                    orient: "bottom",
                    tickSize: 5,
                    tickPadding: 5,
                    tickValues: durations.length
                }}
                axisLeft={{
                    orient: "left",
                    tickSize: 5,
                    tickPadding: 5,
                    legend: "seconds",
                    legendPosition: "middle",
                    legendOffset: -60
                }}
                tooltip={({ node }) => getTooltip(node, props.measurements)}
                legends={[
                    {
                        anchor: "bottom-right",
                        direction: "column",
                        justify: false,
                        translateX: 130,
                        translateY: 0,
                        itemWidth: 100,
                        itemHeight: 12,
                        itemsSpacing: 5,
                        itemDirection: "left-to-right",
                        symbolSize: 12,
                        symbolShape: "circle",
                        effects: [
                            {
                                on: "hover",
                                style: {
                                    itemOpacity: 1
                                }
                            }
                        ]
                    }
                ]}
                data={[
                    {
                        id: "",
                        data: durations
                    }
                ]}
            />
        </div>
    );
};

function getTooltip(node, measurements: IMeasurement[]) {
    return (
        node &&
        node.index > -1 &&
        node.index < measurements.length && (
            <div
                style={{
                    color: "white",
                    background: "#333",
                    padding: "12px 16px"
                }}
            >
                {measurements[node.index].action}
                <br />
                {measurements[node.index].details}
                <br />
                {node.data.formattedY}
            </div>
        )
    );
}
// This control is used only as the top-level component of the page.
// These lines bootstrap react and are run when the PerformanceLogPage.htm processes <script src="/performanceLogBundle.js"></script>
const root = document.getElementById("root");
ReactDOM.render(<PerformanceLogPage />, root);
