import * as React from "react";
import { useState } from "react";
import { BloomApi } from "../../utils/bloomApi";
import { useWebSocketListenerForOneMessage } from "../../utils/WebSocketManager";
import "./PerformanceLogControl.less";

export const PerformanceLogControl: React.FunctionComponent = props => {
    const [rows, setRows] = useState<Array<string[]>>([]);
    const [columns, setColumns] = useState<string[]>([]);
    useWebSocketListenerForOneMessage("performance", "columns", msg => {
        setColumns(msg.split(","));
    });
    useWebSocketListenerForOneMessage("performance", "event", msg => {
        const newRow = msg.split(",");
        console.log(msg);
        setRows(previous => {
            return [...previous, newRow];
        });
    });
    React.useEffect(
        () => {
            // The timeout is here because without it we would not actually receive
            // the message setting the column headers. As if react sent this post
            // before setting up the web socket listener.
            window.setTimeout(() => BloomApi.post("performance/start"), 100);
        },
        [] // run only once
    );

    return (
        <div id="performanceLogControl">
            <table>
                <thead>
                    {columns.map((columnName, index) => (
                        <th key={index}>
                            <th>{columnName}</th>
                        </th>
                    ))}
                </thead>
                <tbody>
                    {rows.map((row, index) => (
                        <tr key={index}>
                            {row.map((item, columnIndex) =>
                                columnIndex < 3 ? ( // 1st 3 columns are not memory values
                                    <td>{item}</td>
                                ) : (
                                    <td
                                        key={columnIndex}
                                        className={`${
                                            Number(item) < 0 ? "negative" : ""
                                        }`}
                                    >
                                        {Number(item) < 0 ? "-" : "+"}
                                        {Number(item).toLocaleString()}
                                    </td>
                                )
                            )}
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
};
