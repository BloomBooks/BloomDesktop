import { first } from "underscore";
import { TimedTextSegment } from "./AdjustTimingsControl";

//Given an initial guess at the sentence breaks, adjustPausesToQuietestNearby looks for the quietest point near each projected break
//  and returns a copy of initialSegments with the splits moved accordingly
//It does this by searching a given fraction of both segments that border a split point, splitting that fraction up into sub-intervals of a given length
//  and evaluating sub-intervals to find the one most likely to represent a sentence break.
//  currently that evaluation is done by finding the sub-interval with the smallest maximum amplitude.

//Enhance: Consider whether swapping out that evaluation function would improve our algorithm's accuracy
//also consider giving sub-intervals a higher weight if they're closer to the initial guess.
//we might also consider whether we can stretch the quiet spot as wide as possible, then place our split int the middle.

//This constant determines how far from the initial splits the method will look for a quiet point.
//For example, if kSegmentFractionToExamine = 0.3, then we'll look at 30% of the segments to either side of the split point
const kSegmentFractionToExamine = 0.3;

//This constant determines the length of each sub-interval in seconds.
const kSubIntervalDuration = 0.1;

export function adjustPausesToQuietestNearby(
    initialSegments: TimedTextSegment[], //A set of timed text segments that has already been aligned by a simpler method such as text length
    audioData: number[], //an array containing the amplitude of each sample of the audio clip
    duration: number, //the duration of the audio clip in seconds
): TimedTextSegment[] {
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
        (amp) => Math.abs(amp) > maxAmplitude * 0.1,
    );
    //adjust is the fraction of the audio that comes after the leading silence
    let adjust = 1;
    if (firstSound > 0 && firstSound < audioData.length * 0.7) {
        adjust = (audioData.length - firstSound) / audioData.length;
    }
    const startOfSoundTime = duration * (1 - adjust);
    //const realDuration = duration * adjust;
    const adjustTime = (time: number): number => {
        // We're going to not count the part of the audioData before firstSound.
        // For example: suppose the first third of the audio is silence,
        // and the first segment starts half way through the total duration.
        // We want it instead to start half way through the non-silent final 2/3.
        return time * adjust + startOfSoundTime;
    };
    const segments = initialSegments.map((s) => ({
        start: adjustTime(s.start),
        end: adjustTime(s.end),
        text: s.text,
    }));
    for (let i = 0; i < segments.length - 1; i++) {
        const seg = segments[i];
        const initialGuessIndex = (audioData.length * seg.end) / duration;
        const numSamplesFromPrevSeg =
            ((seg.end - seg.start) / duration) *
            kSegmentFractionToExamine *
            audioData.length;
        const firstIndexToExamine = initialGuessIndex - numSamplesFromPrevSeg;
        const nextSeg = segments[i + 1];
        const numSamplesFromNextSeg =
            ((nextSeg.end - nextSeg.start) / duration) *
            kSegmentFractionToExamine *
            audioData.length;
        const lastIndexToExamine = initialGuessIndex + numSamplesFromNextSeg;

        const sampleLength = duration / audioData.length;
        const durationToExamine =
            (lastIndexToExamine - firstIndexToExamine) * sampleLength;
        const numberOfSubIntervals = Math.ceil(
            durationToExamine / kSubIntervalDuration,
        );

        const subIntervalScores: number[] = [];
        const numSamplesPerSubInterval =
            (lastIndexToExamine - firstIndexToExamine) / numberOfSubIntervals;
        for (let j = 0; j < numberOfSubIntervals; j++) {
            let max = 0;
            const subIntervalStartIndex = Math.floor(
                firstIndexToExamine + j * numSamplesPerSubInterval,
            );
            const subIntervalEndIndex = Math.floor(
                firstIndexToExamine + (j + 1) * numSamplesPerSubInterval,
            );
            for (let k = subIntervalStartIndex; k < subIntervalEndIndex; k++) {
                max = Math.max(max, Math.abs(audioData[k]));
            }
            subIntervalScores.push(max);
        }
        const bestSubIntervalScore = Math.min(...subIntervalScores);
        const bestSubIntervalIndex =
            subIntervalScores.indexOf(bestSubIntervalScore);
        const newEndTime =
            (Math.floor(
                firstIndexToExamine +
                    (bestSubIntervalIndex + 0.5) * numSamplesPerSubInterval,
            ) *
                duration) /
            audioData.length;
        segments[i].end = newEndTime;
        segments[i + 1].start = newEndTime;
    }
    // It sometimes looks better if we leave the silence out of the first segment,
    // but if we guess wrong, there's no way to get the first split before the start of
    // the first segment. Moreover, until we implement a way to trim the start of the audio,
    // the first segment actually will include it, so this is also more realistic.
    segments[0].start = 0;

    return segments;
}

// For a framework created to test and optimize this kind of algorithm,
// see https://github.com/BloomBooks/Spoken-Sentence-Divider,
// forked from https://github.com/NJStriebel/Spoken-Sentence-Divider.
