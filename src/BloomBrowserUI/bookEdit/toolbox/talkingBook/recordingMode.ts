// TODO: What's a better name so that it can apply to both RecordingMode and PlaybackMode?
// Or maybe you want to make a duplicate enum for playback mode.
// Or maybe your playback mode enum would have a different set of states... TextBox, Sentence, SentenceHardSplit, SentenceSoftSplit
// Or, maybe you should list out in an enum the valid combinations (PureSentence, PureText, TextHardSplit, TextSoftSplit)
// Or you could try to do same as above but using discriminated unions?
//
// Should correspond to the version in "\src\BloomExe\web\controllers\TalkingBookApi.cs"

export enum RecordingMode {
    Unknown = "Unknown",
    Sentence = "Sentence",
    TextBox = "TextBox",
}
