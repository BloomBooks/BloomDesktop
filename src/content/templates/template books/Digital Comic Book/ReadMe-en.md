<!--
Note, much of this is duplicated in the Paper Comic Book template.
If you change the text of something which is comic.template.* but not comic.template.digital.*,
it should probably be changed in both readmes.
-->

# Digital Comic Book {i18n="comic.template.digital.title"}

Note: The Canvas Tool requires that you have a valid <a href="" onclick="fetch('/bloom/api/common/showSettingsDialog?tab=subscription', {method:'POST'})">Bloom subscription</a>. However, you can localize a shellbook that contains canvas elements into your own language without a Bloom subscription. {i18n="comic.template.subscription"}

Use this template as a starting point for comics designed for use on screens. {i18n="comic.template.digital.use"}

## Limitations of Bloom's current comic book support

- As with other text, Bloom's Talking Book Tool allows recording audio for comic bubbles. The checkbox "Show Playback Order buttons" in the Talking Book Tool enables you to define the playback order of the bubbles.
- If you change the paper-size or layout of a book, you may have to adjust the locations of bubbles.
- The text in bubbles is currently limited to a rectangle, rather than conforming to the actual outlines of the bubble. You can use &lt;enter&gt; to manually break lines. Your comic will look more professional if you take the time to resize bubbles to be as tight as possible.
- Comic books can show only one language on an image at the same time.
- If a comic book contains multiple languages (as sources), users can switch between languages in Bloom Reader, the web, etc.

## Tips on using the Canvas Toolbox

- To move a bubble, click on it once, then drag anywhere in the bubble.
- To edit the text in a bubble, click on it once a second time.
- To resize a bubble, grab the resize handle on either side.
- In the rare case that you need to change the curve of a bubble tail, drag the circle that is in the middle of the tail. It will become a solid color to indicate that you have left "auto mode". To return to "auto mode", double click that circle.


## Tips on "lettering" {i18n="comic.template.tips.lettering.header"}

*"Lettering"* is the term used for adding bubbles and text to comics. Take some time to learn what professional *letterers* say about making good-looking comics. Bloom takes care of some of these things for you, but there are many things that require training your eye. See https://blambot.com/pages/lettering-tips. Note, you do *not* need to only use upper case letters. It does look cool because we are used to seeing comics done this way, but you should do whatever is best for your audience. If you do choose upper case *and* have a script that is widely supported, consider using a special comic book font. See [here](https://blambot.com/collections/all-fonts/dialogue) and [here](https://jasonthibault.com/comic-book-fonts/). {i18n="comic.template.tips.lettering"}

## A note on White on Black Text

You can use "Change Layout" to divide the screen and add a text box on the side. The background will be black, so you'll need to select the style "WhiteText".
