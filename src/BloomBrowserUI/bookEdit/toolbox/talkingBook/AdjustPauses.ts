import { TimedTextSegment } from "./AdjustTimingsControl";

const BREAKSPOT_DURATION = 0.1;
const MAX_SPLIT_MOVE_PERCENTAGE = 0.3;

//for a framework created to test and optimize this kind of algorithm, feel free to use and modify anything you find at https://github.com/NJStriebel/Spoken-Sentence-Divider

export function adjustPausesToQuietestNearby(
    initialSegments: TimedTextSegment[],
    audioData: number[],
    duration: number
) {
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

    // Look for leading pause. Here the maxAmplitude * 0.1 and the 0.7 limit
    // are pretty arbitrary. We're assuming the start of what we want will be
    // at least a tenth as loud as the loudest sound in the file.
    // And if the first sound is more than 70% of the way through the file,
    // it seems likely that some very loud sound has confused things, so better
    // not to adjust at all. This also limits how close together the initial
    // splits can get as a result of trying to ignore initial silence.
    let maxAmplitude = 0;
    for (const amp of audioData) {
        maxAmplitude = Math.max(maxAmplitude, Math.abs(amp));
    }
    const firstSound = audioData.findIndex(
        amp => Math.abs(amp) > maxAmplitude * 0.1
    );
    //adjust is the fraction of the audio that comes after the leading silence
    let adjust = 1;
    if (firstSound > 0 && firstSound < audioData.length * 0.7) {
        adjust = (audioData.length - firstSound) / audioData.length;
    }
    const startOfSound = duration * (1 - adjust);
    //const realDuration = duration * adjust;
    const adjustTime = (time: number): number => {
        // We're going to not count the part of the audioData before firstSound.
        // For example: suppose the first third of the audio is silence,
        // and the first segment starts half way through the total duration.
        // We want it instead to start half way through the non-silent final 2/3.
        return time * adjust + startOfSound;
    };
    let segments = initialSegments.map(s => ({
        start: adjustTime(s.start),
        end: adjustTime(s.end),
        text: s.text
    }));
    for (let i = 0; i < segments.length - 1; i++) {
        const seg = segments[i];
        const mid = (audioData.length * seg.end) / duration;
        const slop =
            ((seg.end - seg.start) / duration) *
            MAX_SPLIT_MOVE_PERCENTAGE *
            audioData.length;
        const start = mid - slop;
        const nextSeg = segments[i + 1];
        const nextSlop =
            ((nextSeg.end - nextSeg.start) / duration) *
            MAX_SPLIT_MOVE_PERCENTAGE *
            audioData.length;
        const end = mid + nextSlop;

        const sampleLength = duration / audioData.length;
        const slopDuration = (end - start) * sampleLength;
        const numberOfBreaks = slopDuration / BREAKSPOT_DURATION;

        const breakSpots: number[] = [];
        const breakSpotLength = (end - start) / numberOfBreaks;
        for (let j = 0; j < numberOfBreaks; j++) {
            let max = 0;
            const startBreakSpot = Math.floor(start + j * breakSpotLength);
            const endBreakSpot = Math.floor(start + (j + 1) * breakSpotLength);
            for (let k = startBreakSpot; k < endBreakSpot; k++) {
                max = Math.max(max, Math.abs(audioData[k]));
            }
            breakSpots.push(max);
        }
        const minBreakSpot = Math.min(...breakSpots);
        const breakSpot = breakSpots.indexOf(minBreakSpot);
        const newEnd =
            (Math.floor(start + (breakSpot + 0.5) * breakSpotLength) *
                duration) /
            audioData.length;
        segments[i].end = newEnd;
        segments[i + 1].start = newEnd;
    }
    // It sometimes looks better if we leave the silence out of the first segment,
    // but if we guess wrong, there's no way to get the first split before the start of
    // the first segment. Moreover, until we implement a way to trim the start of the audio,
    // the first segment actually will include it, so this is also more realistic.
    segments[0].start = 0;

    return segments;
}
