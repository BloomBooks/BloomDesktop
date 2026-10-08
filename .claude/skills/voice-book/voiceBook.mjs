/* eslint-env node */
/* global console, process, fetch, Buffer */
// Voices one language of a Bloom book with ElevenLabs text-to-speech, producing a talking book.
//
// Each text box gets one recording, made with ElevenLabs' /with-timestamps endpoint so the
// character timings give the sentence boundaries. The markup written is the same markup Bloom's
// spreadsheet import writes for a text box recorded as a whole and then split
// (SpreadsheetImporter.AddAudioAsync): data-audiorecordingmode="TextBox", audio-sentence and
// bloom-postAudioSplit on the bloom-editable, data-audiorecordingendtimes, and one
// bloom-highlightSegment span per sentence. Sentences are found with Bloom's own splitter and
// checksums are made with Bloom's own getChecksum, both bundled from src/BloomBrowserUI at run
// time, so the Talking Book tool sees exactly the sentences it would have made itself.
//
// Usage: node voiceBook.mjs <book folder> --lang en --voice <voice id> [--model <model id>]
//            [--language-code <ISO 639-1>] [--force] [--dry-run]
// The API key is read from the ELEVENLABS_KEY environment variable.

import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const browserUIDir = path.resolve(
    scriptDir,
    "..",
    "..",
    "..",
    "src",
    "BloomBrowserUI",
);
const browserUIRequire = createRequire(path.join(browserUIDir, "package.json"));
const { JSDOM } = browserUIRequire("jsdom");
const esbuild = createRequire(browserUIRequire.resolve("vite"))("esbuild");

/** Parses the command line into an options object; exits with usage text on bad input. */
function parseArgs(argv) {
    const options = {
        model: "eleven_v4",
        force: false,
        dryRun: false,
    };
    const positional = [];
    for (let i = 0; i < argv.length; i++) {
        const arg = argv[i];
        if (arg === "--lang") options.lang = argv[++i];
        else if (arg === "--voice") options.voice = argv[++i];
        else if (arg === "--model") options.model = argv[++i];
        else if (arg === "--language-code") options.languageCode = argv[++i];
        else if (arg === "--force") options.force = true;
        else if (arg === "--dry-run") options.dryRun = true;
        else positional.push(arg);
    }
    options.bookFolder = positional[0];
    if (
        !options.bookFolder ||
        !options.lang ||
        (!options.voice && !options.dryRun)
    ) {
        console.error(
            "Usage: node voiceBook.mjs <book folder> --lang <bloom lang tag> --voice <voice id> [--model <id>] [--language-code <iso>] [--force] [--dry-run]",
        );
        process.exit(2);
    }
    return options;
}

/** Bundles Bloom's sentence splitter and checksum from source and returns them, running in jsdom. */
async function loadBloomTextFunctions() {
    const result = await esbuild.build({
        entryPoints: [
            path.join(browserUIDir, "spreadsheet", "spreadsheetBundleRoot.ts"),
        ],
        bundle: true,
        format: "iife",
        platform: "browser",
        write: false,
        logLevel: "error",
    });
    const dom = new JSDOM("<!doctype html><body></body>", {
        runScripts: "outside-only",
    });
    dom.window.eval(result.outputFiles[0].text);
    return dom.window.spreadsheetBundle;
}

/** Finds the book's .htm file: the one named after the folder, or else the only one there. */
function findBookHtm(folder) {
    const named = path.join(folder, path.basename(folder) + ".htm");
    if (fs.existsSync(named)) return named;
    const htms = fs
        .readdirSync(folder)
        .filter((f) => f.toLowerCase().endsWith(".htm"));
    if (htms.length !== 1)
        throw new Error(`Expected one .htm in ${folder}, found ${htms.length}`);
    return path.join(folder, htms[0]);
}

/**
 * Returns the bloom-editables to voice, in reading order: the front cover's title, then every
 * text box in the book's own pages (not front or back matter). Image descriptions are left out,
 * as they are hidden unless the book turns them on.
 */
function findTargetEditables(doc, lang) {
    const targets = [];
    for (const page of doc.querySelectorAll("div.bloom-page")) {
        const isFrontCover = page.classList.contains("frontCover");
        const isMatter =
            page.classList.contains("bloom-frontMatter") ||
            page.classList.contains("bloom-backMatter");
        if (isMatter && !isFrontCover) continue;
        const selector = isFrontCover
            ? `.bloom-editable[data-book="bookTitle"][lang="${lang}"]`
            : `.bloom-translationGroup:not(.bloom-imageDescription) > .bloom-editable[lang="${lang}"]`;
        for (const editable of page.querySelectorAll(selector)) {
            if (editable.textContent.trim()) targets.push({ page, editable });
        }
    }
    return targets;
}

/**
 * True if the text box already has a recording: an audio file named after the box or one of its
 * sentences. Markup alone does not count, because the Talking Book tool adds audio-sentence
 * markup to every box it visits, recorded or not.
 */
function hasRecording(editable, audioFolder) {
    const ids = [editable, ...editable.querySelectorAll(".audio-sentence")]
        .map((element) => element.id)
        .filter(Boolean);
    return ids.some((id) =>
        [".mp3", ".wav"].some((ext) =>
            fs.existsSync(path.join(audioFolder, id + ext)),
        ),
    );
}

/** Removes existing talking-book markup, keeping the text (as SpreadsheetImporter.AddAudioAsync does). */
function removeAudioMarkup(editable) {
    for (const attr of [
        "data-duration",
        "data-audiorecordingendtimes",
        "recordingmd5",
        "data-audiorecordingmode",
    ]) {
        editable.removeAttribute(attr);
    }
    editable.classList.remove("bloom-postAudioSplit", "audio-sentence");
    for (const span of editable.querySelectorAll(
        "span.audio-sentence, span.bloom-highlightSegment",
    )) {
        span.replaceWith(...span.childNodes);
    }
}

/** Converts a fragment of book HTML into the plain text to speak. */
function fragmentToSpeech(doc, html) {
    const temp = doc.createElement("div");
    temp.innerHTML = html;
    for (const br of temp.querySelectorAll("br")) br.replaceWith(" ");
    // A "|" marks a phrase split for recording; it is not something to read aloud.
    return temp.textContent.replace(/\|/g, "").replace(/\s+/g, " ").trim();
}

/**
 * Splits a text box into paragraphs of fragments using Bloom's splitter, and builds the text to
 * send to ElevenLabs, remembering where each sentence starts and ends in it.
 */
function planTextBox(doc, editable, bloom) {
    const paragraphs = [...editable.querySelectorAll("p")];
    const containers = paragraphs.length ? paragraphs : [editable];
    const plannedParagraphs = [];
    const sentences = [];
    let speech = "";
    for (const container of containers) {
        const html = container.innerHTML;
        const fragments = html.length
            ? bloom
                  .split(html)
                  .split("\n")
                  .map((f) => ({ isSentence: f[0] === "s", html: f.slice(1) }))
            : [];
        plannedParagraphs.push({ container, fragments });
        let firstInParagraph = true;
        for (const fragment of fragments) {
            if (!fragment.isSentence) continue;
            const text = fragmentToSpeech(doc, fragment.html);
            if (!text) {
                fragment.isSentence = false;
                continue;
            }
            // A newline between paragraphs gives a longer pause than a space between sentences.
            if (speech.length) speech += firstInParagraph ? "\n" : " ";
            firstInParagraph = false;
            fragment.sentence = {
                start: speech.length,
                end: speech.length + text.length,
            };
            speech += text;
            sentences.push(fragment.sentence);
        }
    }
    return { paragraphs: plannedParagraphs, sentences, speech };
}

/** Calls ElevenLabs text-to-speech with timestamps; returns the mp3 bytes and character alignment. */
async function synthesize(options, text, previousText, nextText) {
    const url = `https://api.elevenlabs.io/v1/text-to-speech/${options.voice}/with-timestamps?output_format=mp3_44100_128`;
    const body = { text, model_id: options.model };
    // previous_text/next_text keep the delivery consistent from one text box to the next.
    if (previousText) body.previous_text = previousText;
    if (nextText) body.next_text = nextText;
    if (options.languageCode) body.language_code = options.languageCode;
    const response = await fetch(url, {
        method: "POST",
        headers: {
            "xi-api-key": options.apiKey,
            "Content-Type": "application/json",
        },
        body: JSON.stringify(body),
    });
    if (!response.ok) {
        throw new Error(
            `ElevenLabs ${response.status}: ${await response.text()}`,
        );
    }
    const json = await response.json();
    return {
        mp3: Buffer.from(json.audio_base64, "base64"),
        alignment: json.alignment,
    };
}

/** Measures an mp3's duration in seconds by walking its MPEG audio frame headers. */
function mp3DurationSeconds(buffer) {
    const bitratesV1L3 = [
        0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320,
    ];
    const bitratesV2L3 = [
        0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160,
    ];
    const sampleRates = {
        3: [44100, 48000, 32000],
        2: [22050, 24000, 16000],
        0: [11025, 12000, 8000],
    };
    let offset = 0;
    if (buffer.toString("latin1", 0, 3) === "ID3") {
        offset =
            10 +
            ((buffer[6] << 21) |
                (buffer[7] << 14) |
                (buffer[8] << 7) |
                buffer[9]);
    }
    let seconds = 0;
    while (offset + 4 <= buffer.length) {
        if (buffer[offset] !== 0xff || (buffer[offset + 1] & 0xe0) !== 0xe0) {
            offset++;
            continue;
        }
        const version = (buffer[offset + 1] >> 3) & 3; // 3 = MPEG1, 2 = MPEG2, 0 = MPEG2.5
        const layer = (buffer[offset + 1] >> 1) & 3; // 1 = Layer III
        const bitrateIndex = buffer[offset + 2] >> 4;
        const sampleRateIndex = (buffer[offset + 2] >> 2) & 3;
        const padding = (buffer[offset + 2] >> 1) & 1;
        if (
            version === 1 ||
            layer !== 1 ||
            bitrateIndex === 0 ||
            bitrateIndex === 15 ||
            sampleRateIndex === 3
        ) {
            offset++;
            continue;
        }
        const sampleRate = sampleRates[version][sampleRateIndex];
        const bitrate =
            (version === 3 ? bitratesV1L3 : bitratesV2L3)[bitrateIndex] * 1000;
        const samplesPerFrame = version === 3 ? 1152 : 576;
        const frameLength =
            Math.floor(((samplesPerFrame / 8) * bitrate) / sampleRate) +
            padding;
        seconds += samplesPerFrame / sampleRate;
        offset += frameLength;
    }
    return seconds;
}

/**
 * Turns ElevenLabs' character timings into Bloom's sentence end times. Each boundary is put
 * midway through the pause between one sentence's last character and the next one's first; the
 * final end time is the length of the whole recording.
 */
function sentenceEndTimes(sentences, speech, alignment, duration) {
    if (alignment.characters.length !== speech.length) {
        throw new Error(
            `ElevenLabs aligned ${alignment.characters.length} characters but we sent ${speech.length}`,
        );
    }
    const starts = alignment.character_start_times_seconds;
    const ends = alignment.character_end_times_seconds;
    return sentences.map((sentence, i) => {
        if (i === sentences.length - 1) return duration;
        const next = sentences[i + 1];
        return (ends[sentence.end - 1] + starts[next.start]) / 2;
    });
}

/** Rewrites the text box's paragraphs with one bloom-highlightSegment span per sentence. */
function writeSegmentSpans(doc, plan) {
    for (const { container, fragments } of plan.paragraphs) {
        container.innerHTML = "";
        for (const fragment of fragments) {
            if (fragment.isSentence) {
                const span = doc.createElement("span");
                span.id = newId();
                span.className = "bloom-highlightSegment";
                span.innerHTML = fragment.html;
                container.appendChild(span);
            } else {
                // Whitespace between sentences; drop any formatting tags around it.
                const temp = doc.createElement("span");
                temp.innerHTML = fragment.html;
                container.appendChild(doc.createTextNode(temp.textContent));
            }
        }
    }
}

/**
 * A data-book field (such as the cover title) also lives in #bloomDataDiv, and when Bloom opens
 * the book it refills every copy of the field from there, so markup only on the cover would be
 * lost. Give every other copy, the data div's included, the same content and audio attributes, as
 * Bloom's own save does when the title is recorded in the Talking Book tool.
 */
function copyToOtherCopiesOfField(doc, editable) {
    const field = editable.getAttribute("data-book");
    if (!field) return;
    const lang = editable.getAttribute("lang");
    for (const other of doc.querySelectorAll(
        `[data-book="${field}"][lang="${lang}"]`,
    )) {
        if (other === editable) continue;
        other.innerHTML = editable.innerHTML;
        for (const attr of [
            "id",
            "data-audiorecordingmode",
            "data-duration",
            "data-audiorecordingendtimes",
            "recordingmd5",
        ]) {
            other.setAttribute(attr, editable.getAttribute(attr));
        }
        other.classList.add("audio-sentence", "bloom-postAudioSplit");
    }
}

/** Makes an id that is valid in XHTML, the way Bloom's createValidXhtmlUniqueId does. */
function newId() {
    const id = crypto.randomUUID();
    return /^\d/.test(id) ? "i" + id : id;
}

/** Formats seconds for an attribute, invariantly and without float noise. */
function formatSeconds(seconds) {
    return String(Math.round(seconds * 1000) / 1000);
}

/** Describes where a text box is, for progress messages. */
function describe(page) {
    const number = page.getAttribute("data-page-number");
    return number
        ? `page ${number}`
        : page.getAttribute("data-xmatter-page") || "cover";
}

async function main() {
    const options = parseArgs(process.argv.slice(2));
    options.apiKey = process.env.ELEVENLABS_KEY;
    if (!options.apiKey && !options.dryRun)
        throw new Error("ELEVENLABS_KEY is not set");

    const htmPath = findBookHtm(options.bookFolder);
    const raw = fs.readFileSync(htmPath, "utf8");
    const hadBom = raw.charCodeAt(0) === 0xfeff;
    const hadDoctype = /^﻿?\s*<!doctype/i.test(raw);
    // jsdom reads a byte-order mark as body text, which pushes the whole <head> into <body>.
    const dom = new JSDOM(hadBom ? raw.slice(1) : raw);
    const doc = dom.window.document;
    const bloom = await loadBloomTextFunctions();

    const targets = findTargetEditables(doc, options.lang);
    if (!targets.length)
        throw new Error(
            `No text in language "${options.lang}" found in ${htmPath}`,
        );
    const audioFolder = path.join(options.bookFolder, "audio");
    const plans = targets.map((target) => {
        const skip =
            !options.force && hasRecording(target.editable, audioFolder);
        // Old markup has to go before planning, or its spans would end up inside the new ones.
        if (!skip && !options.dryRun) removeAudioMarkup(target.editable);
        return {
            ...target,
            skip,
            plan: planTextBox(doc, target.editable, bloom),
        };
    });

    let characters = 0;
    for (const { page, plan, skip } of plans) {
        if (!skip) characters += plan.speech.length;
        console.log(
            `${describe(page)}: ${plan.sentences.length} sentence(s)${skip ? " [already voiced, skipping]" : ""}: ${plan.speech}`,
        );
    }
    console.log(
        `${characters} characters to synthesize with ${options.model}.`,
    );
    if (options.dryRun) return;

    fs.mkdirSync(audioFolder, { recursive: true });
    for (let i = 0; i < plans.length; i++) {
        const { page, editable, plan, skip } = plans[i];
        if (skip) continue;
        const { mp3, alignment } = await synthesize(
            options,
            plan.speech,
            plans[i - 1]?.plan.speech,
            plans[i + 1]?.plan.speech,
        );
        const duration = mp3DurationSeconds(mp3);
        const endTimes = sentenceEndTimes(
            plan.sentences,
            plan.speech,
            alignment,
            duration,
        );

        const id = editable.id || newId();
        editable.id = id;
        fs.writeFileSync(path.join(audioFolder, id + ".mp3"), mp3);
        writeSegmentSpans(doc, plan);
        editable.setAttribute("data-audiorecordingmode", "TextBox");
        editable.classList.add("audio-sentence", "bloom-postAudioSplit");
        editable.setAttribute("data-duration", formatSeconds(duration));
        editable.setAttribute(
            "data-audiorecordingendtimes",
            endTimes.map(formatSeconds).join(" "),
        );
        editable.setAttribute(
            "recordingmd5",
            bloom.getMd5(editable.textContent),
        );
        copyToOtherCopiesOfField(doc, editable);
        console.log(
            `${describe(page)}: ${formatSeconds(duration)}s, ends ${endTimes.map(formatSeconds).join(" ")}`,
        );
    }

    let html = dom.serialize();
    if (!hadDoctype) html = html.replace(/^<!DOCTYPE html>/i, "");
    fs.writeFileSync(htmPath, (hadBom ? "﻿" : "") + html, "utf8");
    console.log(`Saved ${htmPath}`);
}

await main();
