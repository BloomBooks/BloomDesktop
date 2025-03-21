/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import "./adjustTimingsControl.less"; // can't use @emotion on shadow dom

import * as React from "react";
import { Fragment, useState, useEffect, useRef } from "react";
// This strange way of importing seems to be necessary to make the WaveSurfer plugin architecture work.
import WaveSurfer from "wavesurfer.js";
import * as WaveSurferNamespace from "wavesurfer.js";

import RegionsPlugin from "../../../node_modules/wavesurfer.js/dist/plugins/regions";

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
            (button: HTMLButtonElement) => (button.textContent = playChar)
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

        const ws = ((WaveSurferNamespace as unknown) as any).create({
            container: "#waveform",
            plugins: [rp],
            minPxPerSec: 100,
            waveColor: "#2292A2",
            progressColor: "#2292A2",
            cursorColor: "#2292A244",
            normalize: true
            // does nothing obvious
            //FontFace: props.fontFamily
        });
        ws.load(props.audioFileUrl);
        setWaveSurfer(ws);
        ws.on("finish", () => {
            // only sometimes happens, probably because of our audioprocessor handler.
            // Even on the last segment it only sometimes happens, but there are cases.
            getPlayButtons().forEach(
                (button: HTMLButtonElement) => (button.textContent = playChar)
            );
        });
        ws.on("decode", () => {
            let segments: Array<{ start: number; end: number; text: string }> =
                props.segments;
            if (props.shouldAdjustSegments) {
                // We're going to fine-tune the segment breaks that we made based on text length.
                // The idea is to look 30% of the length of the adjacent segments eother side
                // of the split, and break that into 15 pieces. We'll look for the quietest spot
                // in that range and move the split to the middle of that quiet spot.
                // The 30% and 15 pieces are my first guess. They seem to work pretty well but
                // we might want to fine-tune them.
                // We might also want to weight things so that similarly quiet spots are preferred
                // if closer to the original guess. Or we might want to stretch the quiet spot
                // as far as we can either way without it getting much louder, and take the middle
                // of that. We might also bias it somehow towards large quiet spots.
                // Then again, this might be good enough.
                const data = ws.decodedData?.getChannelData(0);
                // Look for leading pause. Here the maxAmplitude * 0.1 and the 0.7 limit
                // are pretty arbitrary. We're assuming the start of what we want will be
                // at least a tenth as loud as the loudest sound in the file.
                // And if the first sound is more than 70% of the way through the file,
                // it seems likely that some very loud sound has confused things, so better
                // not to adjust at all. This also limits how close together the initial
                // splits can get as a result of trying to ignore initial silence.
                let maxAmplitude = 0;
                for (const amp of data) {
                    maxAmplitude = Math.max(maxAmplitude, Math.abs(amp));
                }
                const firstSound = data.findIndex(
                    amp => Math.abs(amp) > maxAmplitude * 0.1
                );
                let adjust = 1;
                if (firstSound > 0 && firstSound < data.length * 0.7) {
                    adjust = (data.length - firstSound) / data.length;
                }
                const startOfSound = ws.getDuration() * (1 - adjust);
                //const realDuration = ws.getDuration() * adjust;
                const adjustTime = (time: number): number => {
                    // We're going to not count the part of the data before firstSound.
                    // For example: suppose the first third of the audio is silence,
                    // and the first segment starts half way through the total duration.
                    // We want it instead to start half way through the non-silent final 2/3.
                    return time * adjust + startOfSound;
                };
                const slopPercent = 0.3;
                const breakSpotCount = 15;
                segments = props.segments.map(s => ({
                    start: adjustTime(s.start),
                    end: adjustTime(s.end),
                    text: s.text
                }));
                for (let i = 0; i < segments.length - 1; i++) {
                    const seg = segments[i];
                    const mid = (data.length * seg.end) / ws.getDuration();
                    const slop =
                        ((seg.end - seg.start) / ws.getDuration()) *
                        slopPercent *
                        data.length;
                    const start = mid - slop;
                    const nextSeg = segments[i + 1];
                    const nextSlop =
                        ((nextSeg.end - nextSeg.start) / ws.getDuration()) *
                        slopPercent *
                        data.length;
                    const end = mid + nextSlop;
                    const numberOfBreaks = Math.min(
                        breakSpotCount,
                        end - start
                    ); // paranoia, should always be breakSpotCount
                    const breakSpots: number[] = [];
                    const breakSpotLength = (end - start) / numberOfBreaks;
                    for (let j = 0; j < numberOfBreaks; j++) {
                        let max = 0;
                        const startBreakSpot = Math.floor(
                            start + j * breakSpotLength
                        );
                        const endBreakSpot = Math.floor(
                            start + (j + 1) * breakSpotLength
                        );
                        for (let k = startBreakSpot; k < endBreakSpot; k++) {
                            max = Math.max(max, Math.abs(data[k]));
                        }
                        breakSpots.push(max);
                    }
                    const minBreakSpot = Math.min(...breakSpots);
                    const breakSpot = breakSpots.indexOf(minBreakSpot);
                    const newEnd =
                        (Math.floor(
                            start + (breakSpot + 0.5) * breakSpotLength
                        ) *
                            ws.getDuration()) /
                        data.length;
                    segments[i].end = newEnd;
                    segments[i + 1].start = newEnd;
                }
                // If the user clicks OK, this is what we want to save.
                const endTimes = segments.map(seg => seg.end);
                props.setEndTimes(endTimes);
                // It sometimes looks better if we leave the silence out of the first segment,
                // but if we guess wrong, there's no way to get the first split before the start of
                // the first segment. Moreover, until we implement a way to trim the start of the audio,
                // the first segment actually will include it, so this is also more realistic.
                segments[0].start = 0;
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
                    color: "000000" // transparent
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
                region.on("click", e => {
                    e.stopPropagation();
                    const playButtons = getPlayButtons();
                    if (playQueueRef.current!.length > 0) {
                        stopAndClearQueue();
                        playButtons.forEach(
                            (button: HTMLButtonElement) =>
                                (button.textContent = playChar)
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
                        regionIndex: index
                    });
                    currentRegion = region.element;
                    const playButton = region.element.getElementsByClassName(
                        "playButton"
                    )[0];
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
                            regionIndex: index + 1
                        });
                    }
                });
            });
        });

        ws.on("audioprocess", time => {
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
