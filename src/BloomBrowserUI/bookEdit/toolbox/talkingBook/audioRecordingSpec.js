import AudioRecording from './audioRecording';


describe("audio recording tests", function () {
    it("inserts sentence spans with ids and class when none exist", function () {
        var div = $("<div>This is a sentence. This is another</div>");
        var recording = new AudioRecording();
        recording.makeSentenceSpans(div);
        var spans = div.find("span");
        expect(spans.length).toBe(2);
        expect(spans[0].innerHTML).toBe('This is a sentence.');
        expect(spans[1].innerHTML).toBe('This is another');
        expect(div.text()).toBe('This is a sentence. This is another');
        expect(spans.first().attr('id')).not.toBe(spans.first().next().attr('id'));
        expect(spans.first().attr('class')).toBe('audio-sentence');
        expect(spans.last().attr('class')).toBe('audio-sentence');
    });
    it("retains matching sentence spans with same ids.keeps md5s and adds missing ones", function () {
        var div = $('<div><p><span id="abc" recordingmd5="d15ba5f31fa7c797c093931328581664" class="audio-sentence">This is a sentence.</span> This is another</p></div>');
        var recording = new AudioRecording();
        recording.makeSentenceSpans(div);
        var spans = div.find("span");
        expect(spans.length).toBe(2);
        expect(spans[0].innerHTML).toBe('This is a sentence.');
        expect(spans[1].innerHTML).toBe('This is another');
        expect(div.text()).toBe('This is a sentence. This is another');
        expect(spans.first().attr('id')).toBe('abc');
        expect(spans.first().attr('recordingmd5')).toBe("d15ba5f31fa7c797c093931328581664");
        expect(spans.first().attr('id')).not.toBe(spans.first().next().attr('id'));
        expect(spans.first().attr('class')).toBe('audio-sentence');
        expect(spans.last().attr('class')).toBe('audio-sentence');
    });
    it("retains markup within sentences", function () {
        var div = $('<div><p><span id="abc" class="audio-sentence">This <b>is</b> a sentence.</span> This <i>is</i> another</p></div>');
        var recording = new AudioRecording();
        recording.makeSentenceSpans(div);
        var spans = div.find("span");
        expect(spans.length).toBe(2);
        expect(spans[0].innerHTML).toBe('This <b>is</b> a sentence.');
        expect(spans[1].innerHTML).toBe('This <i>is</i> another');
    });
    it("keeps id with unchanged recorded sentence when new inserted before", function () {
        var div = $('<div><p>This is a new sentence. <span id="abc" recordingmd5="d15ba5f31fa7c797c093931328581664" class="audio-sentence">This is a sentence.</span></p></div>');
        var recording = new AudioRecording();
        recording.makeSentenceSpans(div);
        var spans = div.find("span");
        expect(spans.length).toBe(2);
        expect(spans[0].innerHTML).toBe('This is a new sentence.');
        expect(spans[1].innerHTML).toBe('This is a sentence.');
        expect(div.text()).toBe('This is a new sentence. This is a sentence.');
        expect(spans.first().next().attr('id')).toBe('abc'); // with matching md5 id should stay with sentence
        expect(spans.first().next().attr('recordingmd5')).toBe("d15ba5f31fa7c797c093931328581664");
        expect(spans.first().attr('id')).not.toBe(spans.first().next().attr('id'));
        expect(spans.first().attr('class')).toBe('audio-sentence');
        expect(spans.last().attr('class')).toBe('audio-sentence');
    });
    it("keeps ids and md5s when inserted between", function () {
        var div = $('<div><p><span id="abcd" recordingmd5="qed" class="audio-sentence">This is the first sentence.</span> This is inserted. <span id="abc" recordingmd5="d15ba5f31fa7c797c093931328581664" class="audio-sentence">This is a sentence.</span> Inserted after.</p></div>');
        var recording = new AudioRecording();
        recording.makeSentenceSpans(div);
        var spans = div.find("span");
        expect(spans.length).toBe(4);
        expect(spans[0].innerHTML).toBe('This is the first sentence.');
        expect(spans[1].innerHTML).toBe('This is inserted.');
        expect(spans[2].innerHTML).toBe('This is a sentence.');
        expect(spans[3].innerHTML).toBe('Inserted after.');
        expect(div.text()).toBe('This is the first sentence. This is inserted. This is a sentence. Inserted after.');
        expect(spans.first().attr('id')).toBe('abcd'); // with matching md5 id should stay with sentence
        expect(spans.first().next().next().attr('id')).toBe('abc'); // with matching md5 id should stay with sentence
        expect(spans.first().next().next().attr('recordingmd5')).toBe("d15ba5f31fa7c797c093931328581664");
        // The first span is reused just by position, since its md5 doesn't match, but it should still keep it.
        expect(spans.first().attr('recordingmd5')).toBe("qed");
        expect(spans.first().attr('id')).not.toBe(spans.first().next().attr('id'));
        expect(spans.first().next().attr('id')).not.toBe(spans.first().next().next().attr('id'));
        expect(spans.first().next().next().attr('id')).not.toBe(spans.first().next().next().next().attr('id'));
        expect(spans.first().attr('class')).toBe('audio-sentence');
        expect(spans.last().attr('class')).toBe('audio-sentence');
        expect(spans.first().next().attr('class')).toBe('audio-sentence');
    });

    // We can get something like this when we paste from Word
    it("ignores empty span", function () {
        var div = $('<div><p>This is the first sentence.<span data-cke-bookmark="1" style="display: none;" id="cke_bm_35C"> </span></p></div>');
        var recording = new AudioRecording();
        recording.makeSentenceSpans(div);
        var spans = div.find("span");
        expect(spans.length).toBe(2);
        expect(spans[0].innerHTML).toBe('This is the first sentence.');
        expect(spans[1].innerHTML).toBe(' ');
        expect(spans.first().attr('class')).toBe('audio-sentence');
        expect(spans.last().attr('class')).not.toContain('audio-sentence');
    });

    // We can get something like this when we paste from Word
    it("ignores empty span and <br>", function () {
        var p = $('<p><span data-cke-bookmark="1" style="display: none;" id="cke_bm_35C">&nbsp;</span><br></p>');
        var recording = new AudioRecording();
        recording.makeSentenceSpans(p);
        var spans = p.find("span");
        expect(spans.length).toBe(1);
        expect(spans[0].innerHTML).toBe('&nbsp;');
        expect(spans.first().attr('class')).not.toContain('audio-sentence');
    });

    it("flattens nested audio spans", function () {
        var p = $('<p><span id="efgh" recordingmd5="xyz" class="audio-sentence"><span id="abcd" recordingmd5="qed" class="audio-sentence">This is the first.</span> <span id="abde" recordingmd5="qef" class="audio-sentence">This is the second.</span> This is the third.</span></p>');
        var recording = new AudioRecording();
        recording.makeSentenceSpans(p);
        var spans = p.find("span");
        // Should have removed the outer span and left the two inner ones and added a third one.
        expect(spans.length).toBe(3);
        expect(spans.first().attr("id")).toBe("abcd");
        expect(spans.first().next().attr("id")).toBe("abde");
        expect(spans[0].innerHTML).toBe('This is the first.');
        expect(spans[1].innerHTML).toBe('This is the second.');
        expect(spans[2].innerHTML).toBe('This is the third.');
        expect(spans.first().attr('class')).toBe('audio-sentence');
        expect(spans.first().next().attr('class')).toBe('audio-sentence');
        expect(spans.first().next().next().attr('class')).toBe('audio-sentence');
    });

    it("ensures full span coverage of paragraph", function () {
        // based on BL-6038 user data
        var p = $('<p>Random text <strong><span data-duration="9.400227" id="abcd" class="audio-sentence" recordingmd5="undefined"><u>underlined</u></span></strong> finish the sentence. Another sentence <u><strong>boldunderlined</strong></u> finish the second.</p>');
        var recording = new AudioRecording();
        recording.makeSentenceSpans(p);
        var spans = p.find("span");
        // Should have expanded the first span and created one for the second sentence.
        expect(spans.length).toBe(2);
        expect(spans.first().attr("id")).toBe("abcd");
        // expect(spans.first().next().attr("id")).toBe("abde");
        expect(spans[0].innerHTML).toBe('Random text <strong><u>underlined</u></strong> finish the sentence.');
        expect(spans[1].innerHTML).toBe('Another sentence <u><strong>boldunderlined</strong></u> finish the second.');
        expect(spans.first().attr('class')).toBe('audio-sentence');
        expect(spans.first().next().attr('class')).toBe('audio-sentence');
    });
});
