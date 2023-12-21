import "./adjustTimingsControl.css"; // can't use @emotion on shadow dom

import * as React from "react";
import { Fragment, useState, useEffect, useRef } from "react";
import WaveSurfer from "wavesurfer.js";
import * as WaveSurferNamespace from "wavesurfer.js";

//I could not get webpack to find this or any of the various versions of the file: import RegionsPlugin from "wavesurfer.js/dist/plugins/regions";
//So I've copied this in
import RegionsPlugin from "./regions";

import splat from "./splat.mp3"; // sound curtesy zapsplat.com

export type TimedTextSegment = {
    start: number;
    end: number;
    text: string;
};

export const AdjustTimingsControl: React.FunctionComponent<{
    url: string;
    segments: Array<{ start: number; end: number; text: string }>;
    setEndTimes: (endTimes: number[]) => void;
}> = props => {
    type playSpan = { start: number; end: number; regionIndex: number };
    type playQueue = Array<playSpan>;
    const [waveSurfer, setWaveSurfer] = useState<WaveSurfer>();
    const waveSurferRef = useRef<WaveSurfer>();
    waveSurferRef.current = waveSurfer;
    const [playQueue] = useState<playQueue>([]);
    const playQueueRef = useRef<playQueue>();
    playQueueRef.current = playQueue;

    function stopAndClearQueue() {
        playQueueRef.current = [];
        waveSurferRef.current!.pause();
    }

    useEffect(() => {
        const rp = RegionsPlugin.create();

        function enqueue(playSpan: playSpan) {
            playQueueRef.current!.push(playSpan);
            // if this is the only item in the queue, start playing
            if (playQueueRef.current!.length === 1) {
                /* somehow touching the style prevents wavesurfer seeing the the mouse up click, or something like that
        const text = document.getElementById("region-" + playSpan.regionIndex);
        text!.style.fontWeight = "bold";
        */
                waveSurferRef.current!.setTime(playSpan.start);
                waveSurferRef.current!.play();
            }
        }

        //const regionColors = ["red", "green", "blue", "yellow", "orange"];

        const ws = ((WaveSurferNamespace as unknown) as any).create({
            container: "#waveform",
            plugins: [rp],
            minPxPerSec: 100,
            waveColor: "#2292A2",
            progressColor: "#2292A2",
            cursorColor: "#2292A244"
        });
        ws.load(props.url);
        setWaveSurfer(ws);
        ws.on("decode", () => {
            rp.clearRegions();
            props.segments.forEach((segment, index: number) => {
                const region = rp.addRegion({
                    content: segment.text,
                    start: segment.start,
                    end: props.segments[index + 1]?.start || 99999,
                    drag: false,
                    resize: index < props.segments.length - 1, // don't let the end of the last region be resized
                    //color: regionColors[index % regionColors.length]
                    color: "000000" // transparent
                });
                region.on("click", e => {
                    e.stopPropagation();
                    if (playQueueRef.current!.length > 0) {
                        stopAndClearQueue();
                    } else {
                        enqueue({
                            start: region.start,
                            end: region.end,
                            regionIndex: index
                        });
                    }
                });
                // after dragging, adjust the start time of the next region and the end time of the previous region
                region.on("update-end", () => {
                    const nextRegion = rp.getRegions()[index + 1];
                    if (nextRegion) {
                        nextRegion.start = region.end;
                        region;
                        nextRegion.setOptions({
                            ...nextRegion,
                            start: region.end
                        });
                    }
                    const prevRegion = rp.getRegions()[index - 1];
                    if (prevRegion) {
                        prevRegion.end = region.start;

                        prevRegion.setOptions({
                            ...prevRegion,
                            end: region.start
                        });
                    }
                    const endTimes = rp.getRegions().map(region => region.end);
                    props.setEndTimes(endTimes);
                    stopAndClearQueue();
                    if (index > 0) {
                        enqueue({
                            start: region.start - 0.5,
                            end: region.start,
                            regionIndex: index - 1
                        });
                    }
                    // now play from the start the region
                    enqueue({
                        start: region.start,
                        // until 1 second later
                        // end: region.start + 1,
                        // to the end
                        end: region.end,
                        regionIndex: index
                    });
                });
            });
        });

        ws.on("audioprocess", time => {
            const currentSpan = playQueueRef.current?.[0];
            if (currentSpan && time > currentSpan.end) {
                ws.pause();
                //dequeue
                playQueueRef.current?.shift();
                if (playQueueRef.current!.length > 0) {
                    const audio = new Audio(splat);
                    const waitForTick = true; // I can't decide which I like better

                    if (waitForTick) {
                        audio.play().then(() => {
                            ws.setTime(playQueueRef.current![0].start);
                            ws.play();
                        });
                    } else {
                        audio.play();
                        // now play the next time
                        ws.setTime(playQueueRef.current![0].start);
                        ws.play();
                    }
                }
            }
        });
        return () => {
            ws.destroy();
        };
    }, [props.segments]);

    return (
        <Fragment>
            <div id="waveform"></div>
        </Fragment>
    );
};
