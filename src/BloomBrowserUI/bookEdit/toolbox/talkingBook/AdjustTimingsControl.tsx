import { css } from "@emotion/react";

import "./adjustTimingsControl.less"; // can't use @emotion on shadow dom

import * as React from "react";
import { Fragment, useState, useEffect, useRef } from "react";
import WaveSurfer from "wavesurfer.js";

import RegionsPlugin from "../../../node_modules/wavesurfer.js/dist/plugins/regions";
import { adjustPausesToQuietestNearby } from "./AdjustPauses";

export type TimedTextSegment = {
    start: number;
    end: number;
    text: string;
};

let currentRegion: Element | undefined;

export const AdjustTimingsControl: React.FunctionComponent<{
    audioFileUrl: string;
    segments: Array<{ start: number; end: number; text: string }>;
    setEndTimes: (endTimes: number[]) => void;
    fontFamily: string;
    shouldAdjustSegments: boolean;
}> = (props) => {
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

    const playChar = "▷";
    function getShadowRoot() {
        const waveform = document.getElementById("waveform");
        if (!waveform) return undefined;
        return waveform.firstElementChild?.shadowRoot;
    }
    function getPlayButtons() {
        const shadowRoot = getShadowRoot();
        if (!shadowRoot) return [];
        return Array.from(shadowRoot.querySelectorAll(".playButton"));
    }
    function playEnded() {
        const playButtons = getPlayButtons();
        playButtons.forEach(
            (button: HTMLButtonElement) => (button.textContent = playChar),
        );
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

        // This horrible hack is the only way I could find to set the font for the regions.
        // Can't set it in adjustTimingsContro.css because it's variable.
        // Can't find anything I can set in the emotion CSS for #wrapper that seems make the right
        // ::part(region) selector work.
        let styleElt = document.getElementById("adjustTimingsControlStyles");
        if (!styleElt) {
            styleElt = document.createElement("style");
            styleElt.id = "adjustTimingsControlStyles";
            document.head.appendChild(styleElt);
        }
        styleElt.innerHTML = `
            ::part(region) {
                font-family: ${props.fontFamily};
            }
        `;

        const ws = WaveSurfer.create({
            container: "#waveform",
            plugins: [rp],
            minPxPerSec: 100,
            waveColor: "#2292A2",
            progressColor: "#2292A2",
            cursorColor: "#2292A244",
            normalize: true,
            // does nothing obvious
            //FontFace: props.fontFamily
        });
        ws.load(props.audioFileUrl);
        setWaveSurfer(ws);
        ws.on("finish", () => {
            // only sometimes happens, probably because of our audioprocessor handler.
            // Even on the last segment it only sometimes happens, but there are cases.
            getPlayButtons().forEach(
                (button: HTMLButtonElement) => (button.textContent = playChar),
            );
        });
        ws.on("decode", () => {
            let segments: Array<{ start: number; end: number; text: string }>;
            if (props.shouldAdjustSegments) {
                // This used to work without the cast but now decodedData is showing up as private.
                const data = (ws as any).decodedData?.getChannelData(0);
                segments = adjustPausesToQuietestNearby(
                    props.segments,
                    data,
                    ws.getDuration(),
                );
                props.setEndTimes(segments.map((seg) => seg.end));
            } else {
                segments = props.segments;
            }

            rp.clearRegions();
            segments.forEach((segment, index: number) => {
                const region = rp.addRegion({
                    content: segment.text,
                    start: segment.start,
                    end: segments[index + 1]?.start || 99999,
                    drag: false,
                    resize: index < segments.length - 1, // don't let the end of the last region be resized
                    //color: regionColors[index % regionColors.length]
                    color: "000000", // transparent
                });
                // This function is immediately invoked, but in case the region isn't ready yet, we'll try again until it is.
                const makePlayButton = (region: any) => {
                    if (!region.element) {
                        setTimeout(() => {
                            makePlayButton(region);
                        }, 100);
                        return;
                    }
                    const playButton = document.createElement("button");
                    playButton.textContent = playChar;
                    playButton.style.position = "absolute";
                    // In the middle it tends to get lost inthe waveform and also obscure it.
                    // Also if the first segment is long it might be off the screen.
                    //playButton.style.top = "calc(50% - 15px)";
                    //playButton.style.left = "calc(50% - 15px)";
                    // At the top it would interfere with the text.
                    playButton.style.bottom = "0";
                    playButton.style.left = "0";
                    playButton.style.height = "30px";
                    playButton.style.width = "30px";
                    playButton.style.borderRadius = "50%";
                    playButton.style.backgroundColor = "transparent";
                    playButton.style.border = "none";
                    playButton.style.color = "#d65649"; // same red as the dotted line
                    playButton.style.fontSize = "20px";
                    playButton.style.zIndex = "1000";
                    playButton.classList.add("playButton");
                    // The play buttons don't actually do anything. They are just a visual hint
                    // that you can play and pause, which actually happen on a click
                    // anywhere in the waveform area.
                    region.element.appendChild(playButton);
                };
                makePlayButton(region);
                region.on("click", (e) => {
                    e.stopPropagation();
                    const playButtons = getPlayButtons();
                    if (playQueueRef.current!.length > 0) {
                        stopAndClearQueue();
                        playButtons.forEach(
                            (button: HTMLButtonElement) =>
                                (button.textContent = playChar),
                        );
                        if (region.element === currentRegion) {
                            currentRegion = undefined;
                            return;
                        }
                        // if we clicked in a different region we'll go ahead and start playing that.
                    }
                    // there seems to be a case where a different segment is playing and needs its pause button reset
                    // I think there isn't necessarily anything in the queue when we're playing.
                    playEnded();
                    enqueue({
                        start: region.start,
                        end: region.end,
                        regionIndex: index,
                    });
                    currentRegion = region.element;
                    const playButton =
                        region.element.getElementsByClassName("playButton")[0];
                    if (playButton) {
                        playButton.textContent = "❚❚";
                    }
                });
                // after dragging, adjust the start time of the next region and the end time of the previous region
                region.on("update-end", () => {
                    const nextRegion = rp.getRegions()[index + 1];
                    if (nextRegion) {
                        nextRegion.start = region.end;
                        //region; lint complained; seems to do nothing.
                        nextRegion.setOptions({
                            ...nextRegion,
                            start: region.end,
                        });
                    }
                    const prevRegion = rp.getRegions()[index - 1];
                    if (prevRegion) {
                        prevRegion.end = region.start;

                        prevRegion.setOptions({
                            ...prevRegion,
                            end: region.start,
                        });
                    }
                    const endTimes = rp
                        .getRegions()
                        .map((region) => region.end);
                    props.setEndTimes(endTimes);
                    stopAndClearQueue();

                    // play the last bit before the divider
                    // enqueue({
                    //     start: Math.max(region.end - 1, region.start),
                    //     // until 1 second later
                    //     // end: region.start + 1,
                    //     // to the end
                    //     end: region.end,
                    //     regionIndex: index
                    // });
                    // play the next bit after the divider
                    if (index < props.segments.length - 1) {
                        const nextRegion = rp.getRegions()[index + 1];
                        enqueue({
                            start: nextRegion.start,
                            end: Math.min(nextRegion.start + 1, nextRegion.end),
                            regionIndex: index + 1,
                        });
                    }
                });
            });
        });

        ws.on("audioprocess", (time) => {
            const currentSpan = playQueueRef.current?.[0];
            if (currentSpan && time > currentSpan.end) {
                ws.pause();
                playEnded(); // any button that is pause should be reset to play
                //dequeue
                playQueueRef.current?.shift();
                if (playQueueRef.current!.length > 0) {
                    ws.setTime(playQueueRef.current![0].start);
                    ws.play();
                    //}
                }
            }
        });

        return () => {
            ws.destroy();
        };
    }, [props.segments, props.fontFamily, props.audioFileUrl]);

    return <div id="waveform"></div>;
};
