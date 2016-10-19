## 3.7
- You can now "unlock" a shell book that you are translating, so that you can do things like:
 - add pages
 - delete pages
 - edit fields that are normally locked during translation

    To unlock the book, go into the toolbox (which lives to the right of the page in the Edit Tab). Click on the icon of gear, and tick the checkbox there. The book will remain unlocked only during the current editing session; the next time you come back to this book, it will be locked again.

- A new "Branding" collection setting allows organizations to specify logos, copyright, and license.

- The Copyright & License box now allows you to select the "Intergovernmental" version of the Creative Commons License.

- Leveled Readers now display the average sentence length, and you can set a maximum average sentence length for the book.

- When you drag the splitters to resize elements on the page, Bloom now shows a percentage indicator. You can use this number to set splitters to the same value on different pages, so that elements line up when they are on facing pages in the printed book.

- If you don't have Keyman or the like running, then when you hold down a key, Bloom shows the "Special Characters" panel which show variants of the key you pressed. Normally, you use your mouse to aim at the one you want. However some laptops are now disabling the touchpad when a key is down. So with we now display little shortcut characters next to each choice. Pressing the indicated key selects the choice.

- When the page was zoomed in an using a smaller screen, the Special Characters panel was sometimes off-screen. Now it's always visible.

- When you do Help:Report a Problem, Bloom now has a better approach to sending us the problem book, so that you can send us larger books than before.

- Previously, some text-heavy parts of the interface (like the descriptions of various Reader templates and how to use them) could not be translated into a different language. Now they can.

- A new "Super Paper Saver" Front/Back Matter pack puts the title page on the inside of the front cover, and the credits on the inside of the back cover.

- If no topic is set, the language name on the front cover is now centered._

- Over 170 fixes and small improvements.

## 3.6
- This version focuses on what we are calling "the toolbox": a panel on the right side that you can open to do specialized tasks like working on decodable and leveled readers or recording audio for "talking books".

- You can now hide or show the toolbox

- "Talking Book" recording is now controlled from a panel in the toolbox

- When setting up a new collection, Bloom now allows you to change the font.

- When setting up a collection, Bloom now always allows you to set the name of the project.

- "Add Page" button is now brighter and easier to discover.

- You can now "Undo" when in "Change Layout" mode.

- In the Format box, you can now type in any arbitrary font size. Previously, you were limited to choosing from the offered set of sizes. Note: you must press Enter after typing in size you want.

- Administrators of computer labs can now install Bloom for all users of the machine. From a command prompt running as administrator, run the installer with the "--allUsers" flag. "--silent" is also supported. This will put Bloom in the Program Files(x86) directory. Bloom will not attempt to update itself, nor will users be able to cause an update to happen.

- Many other fixes & tiny improvements.

## 3.5
- You can now copy and cut images
- You can now hover over an image to get its dimensions, dots-per-inch, and color depth
- Bloom uses less memory when you have very large image files
- Better image memory handling
- Book folders no longer accumulate files for images that you aren't using anymore
- Can now delete source books downloaded from BloomLibrary.org
- Faster bloomlibrary.org downloads
- New Arithmetic Template
- Better error report when the operating system blocks Bloom from touching a file due to weird file permissions
- New experimental EPUB option in Publish Tab (Enable from Settings:Advanced)
- New experimental Talking Book recording/publishing. Enable from Settings:Advanced. To start, right-click in text and choose "Record Audio".
- Fixed Art Of Reading forgetting what language you want to search with.
- Fixed problem where black and white (1 bit) images were converted to 32 bit
- Fixed problem with publishing from a network drive
- Fixed problem where blank lines would disappear
- Several other small fixes

## 3.4
- If after creating a page you decide that you want a different layout than the one you original chose, you can now select "Choose Different Layout" and select a different one.
- If you have the latest [Art Of Reading](http://bloomlibrary.org/#/artofreading) installed (version 3.1), then you can now search for pictures in one of: Arabic, Chinese, Bengali, English, French, Hindi, Indonesian, Portuguese, Spanish, Swahili, and Thai.
- Bloom now supports Letter, HalfLetter, and QuarterLetter (North American) paper sizes.
- Fixed Problem where blank lines were removed.
- Dozens of Fixes.

## 3.3
- All basic pages are now "customizable". That means you can change the relative size of elements on the page, for example making the picture bigger and the text area smaller. You can also click the "Change Layout" button to add new elements to the page.

- When you select some text, Bloom now shows a small popup with buttons for bold, underline, and italic.

- Previously, Bloom used screen space to show a list of available pages you could add. Now, we've freed up that precious space and instead there is an "Add Page" button you click to see a list of pages you can add.

- The toolbar now rearranges itself as need so that everything is available on very small screens (netbooks).

- Books now default license to CC-BY (requested by All Children Reading).

- Books made from Leveled Reader template delivered via Leveled Reader Bloom Pack now have all the formatting locked down, preventing writers from altering the font, size, or spacing of the text (requested by All Children Reading).


## 3.2

- Support new Decodable Readers workflow. Instead of defining a set of letters plus sight words for each Decodable Stage, Bloom now allows you to specify one or more text files of "Allowed Words".
  - These must be simple, unicode text files. Other formats are not supported: Word, LibreOffice, Excel, etc.
  - We have found that 1000 words works fine even on an old slow laptop. If you have much larger lists and a slow computer, there may be some lag while editing. Please let us know if this is a problem for anyone.
  - Complex characters in unicode can be [coded in more than one way](https://en.wikipedia.org/wiki/Unicode_equivalence). At this point, if the word list has a word code in one way, and the text in the book was entered a different way, Bloom will not recognize the word.  Please let us know if this is a problem for anyone.

- Bloom's Edit tab now always returns you the last page you were viewing in a book. This was a [UserVoice Request](https://bloombooks.uservoice.com/forums/153625-general/suggestions/6986831-open-to-the-last-edited-page-in-a-book)

- "Source text bubbles" in shell books have received a much needed makeover, including real tabs in a drop-down list of languages for books that have many source languages.

- In the set up dialog for Decodable Stages, Bloom now outlines letters from previous stages in orange. Previously, they were only bold and some users didn't notice them.

- In the Collections tab, the selected book now displays a little triangle. Clicking on that shows a menu of things you can do with the book. More advanced items are still only available by right-clicking on the book.

- That Book menu now offers a "Copy Book" command.

- Both "Factory" and "Traditional" Front Matter packs now set the first content page to "1"


## 3.1
Version 3.1 was a disciplined sustained & effort to improve hundreds of "little things" that could confuse or block people in certain situations from getting books created, translated, or printed. It also represents the first version where the Linux (Trusty and Precise) version is very close to parity with the Windows version.

## Important Notices

### Andika Replaced By "Andika New Basic" 
- Many Bloom collections use SIL's free "Andika" literacy font as their default typeface. When you create a PDF using Andika, styles such as bold, italic etc. are lost. This will happen with any font that doesn't include a real bold/italic/etc face. This problem is outside of our control at this time (we've reported it to Mozilla). Happily, SIL has released a subset of Andika named [Andika New Basic](http://scripts.sil.org/cms/scripts/page.php?item_id=Andika_New_Basic). Bloom now installs this font for you and uses it by default. If your language is not fully supported by Andika New Basic, please choose another font, ideally one which includes built-in bold and italic. You can test your font by using CTRL+B to make some text bold, then going to publish and looking to see if bold makes it through to the PDF.
- Note, Bloom will automatically change the default font from Andika to Andika New Basic, if you have Andika set. It will only do this once, so if you choose to change it back to Andika, it won't keep changing it. Note also that Bloom will not attempt to change any custom styles you may have created. If you need bold/italics to show up in a custom style, you'll have to change away from Andika by hand.

### Keyman 9
- If you enable Keyman 9 while on a page, you'll need to switch to another page and back before you can type. If this is causing you problems, please [post a suggestion here](http://bloomlibrary.org/#/suggestions) for us to do more work on this.

### Printing Quality
- To get good printing reliability on Windows, install the [Free Adobe Reader](http://get.adobe.com/reader/enterprise/) software. On Linux, we are switching to the system's default way of printing PDFs (which is normally GhostScript). On Windows, having Adobe Reader installed will also take care of some problems with showing images in preview of the PDF.

### The end of the line for Windows XP
- Starting with Bloom 3.1, Bloom will no longer run on Windows XP. Sorry! As Microsoft has retired support for XP, it has become difficult for us to be limitted to program bits that work on XP. But Bloom 3.0 will continue to be available and works just fine. We don't expect there to be any problems with someone using Bloom 3.0 on XP using books created by other people using Bloom 3.1.

## 3.1 Beta 3
- Running the installer again does an uninstall/reinstall
- Added privacy notice to "Report A Problem" dialog
- Allow user to control which languages are advertised on bloomlibrary.org, when uploading
- Show source language names even if they aren't part of the collection
- Fixed incorrect margins in PDFs
- Andika New Basics now part of Linux package
- 61 other minor fixes/improvements

## 3.1 Beta 2
- Improved feedback during application updating
- Various memory-use improvments (more to come)
- Fix overlapping text bubbles
- Fix page numbering
- 52 other minor fixes/improvements
- New faster installer with automatic incremental upgrades. This will be getting some more attention for the next beta, including a re-install capability.
- Page thumbnails now show an "attention" icon if some text on the page overflows its box

## 3.1 Beta 1
- Includes the new "Andika New Basic" font, which includes real bold and italic faces. PDFs made with this show bold and italic, where PDFs created with "Andika" do not.
- CTRL+Click does a paste, and the pasted material is cleaned up, removing the extraneous line breaks that you get when copying out of a PDF.
- Window remembers un-maximized size and placement
- New Indonesian localization
- Updated Arabic, French, & Spanish localizations
- Added description texts to Leveled and Decodable Reader templates
- Added links to training videos in the Help menu
- Book name now shown in the title bar
- Higher quality creative commons logos
- Warns if your collection is in Dropbox
- Pasted images are now named "image1",2,3, etc.
- Improved support for Paragraph-oriented fields (indention, numbers, prefixes) in hand-made templates.
- Support for text wrapping around images and captions in hand-made templates.
- When pasting large color images from Libre Office, automatically switch to jpeg if that will keep the file size small
- Hover over an image to see its file name, size, and dimensions
- We've reinstated integrations with the [Free Adobe Reader](http://get.adobe.com/reader/enterprise/) after the open source alternative we used in 3.0 proved unreliable. It is still available as a fall-back in situations where Adobe Reader is unavailable. On Linux, we are switching to the system's default way of printing (which is normally GhostScript).
- In a field with the "RequireParagraphs" flag, pressing tab inserts an emspace instead of moving to the next tab.

### Bloom 3.0 is the last version for Windows XP
- Starting with Bloom 3.1, Bloom will require Windows Vista, 7, 8, 10, etc.
### Fixes
- Can now type with KeyMan 9 if (and only if) it was turned on before displaying the current page. In a future release we'll remove that requirement.
- Fixed Booklet production of A6 Books
- Make Format dialog more localizable
- Fixed problem when deleting a box in a custom page.
- Fixed Image License changes made directly in Bloom are lost
- Fixed Text size is different on title page: "Language" is smaller than "Topic"
- Fixed Vertical Scroll Bars showing prematurely on small boxes in custom page
- Fixed insufficient space for French labels on thumbnails
- Fixed Font size of "second language" Book Title is lost after re-opening
- Many fixes related to book topic and different languages
- Overflow detector misfires on right to left text.
- Reinstated Chinese UI translation.
- Bloom no longer saves metadata you enter back to the original image. Too dangerous.
- Can choose TIFF images again.
- Bolding or underlined parts of words would introduce unwanted spaces.
- Stopped "Source Bubbles" from overlapping with other stuff by saying that unless the field takes up the full width of the page, only show the bubble when the cursor is in the field.
- Fixed problem with changing the collection name.
- Pasted images are now named "image 1", "image 2", etc., instead of having random names.
- Vaccninations book had lost its Creative Commons license image.
- Fixed problem with saving meta data into certain jpeg images.
- [Decodable Reader] If you change the letters of a stage then close the dialog by clicking "OK", the toolbox tool doesn't update to show the new letters
- [Decodable Reader] If a letter is in a letter combination in current the stage, DR should not automatically allow its consituent letters
- Bloom's feature of making white transparent makes pdfs look awful in some previewers.
- Calendar Title Page font-changing widget in "Funding" box is non-functional
- Fixed bold items not using the same font as its surroundings when styles are customized.
- [Linux] Right-click on Main Tabs & other Menu Bar Selections causes focus shift
- [Linux] Edit page (sometimes) needs multiple refreshes
- [Linux] "Report A Problem" can't report directly, has to go through email.
- [Linux] Art Of Reading instructions truncated
- [Linux] Problem typing in Art Of Reading search box
- [Linux] Some interfaces languages are listed multiple times.
- [Linux] Double clicking on a BloomPack didn't run Bloom

## 3.0.106 Version Stable Release
- Fix problem with downloading Kande's story
- Allow letters and sight words in Decodable Reader tool to be entered separated by commas
- For tall scripts like Devanagari, automatically increase the minimum height of fields to the line height

## 3.0.103 Version Stable Release
- Added links to training videos in the Help Menu.

## 3.0.102 Version Stable Release
- Update French and Spanish User Interface localizations.
- Add explanatory texts to decodable and leveled reader templates.
- Fixed an error in the "Key Concepts" document.
- Decodable reader will now complain about words with letter combinations.(like "ch") if they are defined but haven't been taught at the current level, but only the consituents (like "c" and "h") have.
- Fix display of a page number count in leveled reader tool.

## 3.0.101 Version Stable Release
- Adds Thai User Interface localization
- Adds Lao User Interface localization
- Fixed rare error when a page is being saved

## 3.0.100 Version Stable Release

## 3.0.97 Beta
- Update French UI Translation (thanks David Rowe)
- When importing, Bloom no longer
  - makes images transparent when importing.
  - compresses images transparent when importing.
  - saves copyright/license back to the original files
- Fix crash after closing settings dialog when no book is selected
- Fix insertion of unwanted space before bolded, underlined, and italicized portions of words
- Fix creative commons license on Vaccinations sample shell

### Fixes
- Spaces are no longer inserted between bold or underlined parts of a word and the normal parts
- Fixed a problem that prevent renaming a collection
- Fixed Vaccination shell Creative Commons logo

## 3.0.93 BETA

### A couple known problems
- If upgrading from Bloom 2, the Windows installer loses one of Bloom's files. It will now notify you that this happened and ask you do reinstall and choose "repair".
- We discovered that books with very large illustrations (e.g. 5 mb color files) are breaking the PDF'ing system. Bloom now detects this and gives you pointers on how to work around the problem, until Bloom itself can do so, in the future.


### Fixes
- Fixed text in calendar day boxes
- Calendar grid lines are now uniformly thin
- Fixed a occasional crash when switching to a different user-interface language
- Tweak xmatter stuff to ease creating custom xmatter from installer

## 3.0.88 BETA

- Users upgrading from Bloom 2 may need to uninstall first, or run the Bloom 3 installer twice. A message with instructions now appears if this is necessary.

- [UserVoice Suggestion] Introduction of A6 Portrait option. **Feedback appreciated**

### Front/Back Matter

#### Cover Page
- Fields on the Cover can now grow to fit however many lines you neeed, because...
- The image on the Cover page  will now automatically shrink so that whatever text you need can fit
- Front cover & title page can now show title in all 3 languages

#### Title Page
- [UserVoice Suggestion] Title page's funding box can now grow, to use for a cheap Table Of Contents if Needed

#### Credits Page
- The Credits page now has more room for acknowledgments
- When selecting a Front/Back Matter pack in the Settings dialog, you can now read a short description of each one
- When you select a different Front/Back Matter pack, existing books will automatically use it if appropriate

#### Other Front/Back Matter
- Trial of a Front/Back Matter pack for SIL Cameroon, which is like "Traditional" but includes the ISO 639 code of the language
- Big Books now use the same Front/Back Matter pack as the rest of the collection. You can now delete the "instructions for teachers" page if you don't want it

### Other
- Big Books now include the "Custom" page template
- Removed the "A5 Portrait Bottom Align" option
- New shortcut keys (In the future we expect to introduce UI buttons for these things, but we are delayed because we need to do it without making the UI more complex):
  - CTRL+R: right-align
  - CTRL+L: left-align
  - CTRL+SHIFT+E: center text
  - ALT+CTRL+0: Normal
  - ALT+CTRL+1: Heading 1
  - ALT+CTRL+2: Heading 2

### Fixes
- Bloom will now be patient if Dropbox is temporarily locking the langaugedisplay.css file
- Title page now updates immeditatley if you change country/province in Settings Dialog
- Format dialog tooltips no longer make Source Bubbles disappear
- Improved error messages when an html file can't be opened
- Thai script is now larger in shell book source bubbles

### Linux Fixes
- Can now open book downloaded from Bloom Library
- Fonts are now listed in alphabetical order
- Crash due to deleted temporary html file has been fixed.
- We are working on a problem typing in the "Report Problem" dialog box.

## 3.0.80 BETA
- Fix: "Open in Firefox" should now work even if you have spaces in the path
- Fix: BigBook National Language title was missinga parenthesis
- Fix: Was hard to insert an uppercase character using the long-press special character feature
- Fix: "Update Book" messed up custom pages
- Several Linux-only fixes 

## 3.0.74 BETA

- A new Front/Backmatter option is available, named "Traditional". This puts the credits page on the back of the title page, rather than the inside of the front cover. This is good in cases where you pay by the page imprint, rather than by pieces of paper. You can select it from Settings:Book Making:Front/Back Matter Pack. <s>Once you have chosen this, you won't see it on any existing books until you do these steps: In the Collection Tab, click on the little triangle, then select "Advanced: Do Updates of All Books".</s>
## 3.0.72 BETA

- More French coverage of UI
- Better captioning of books with long names
- Calendar now has smaller margins, more room for text in day boxes
- Restored vertical centering in "just text" page

### Known Issues
- Fonts with modifiers like "Arial Narrow" cannot be shown. This appears to be a bug in Firefox (which is at the heart of Bloom). So for now we don't offer these fonts in the font-picking menu.

## 3.0.70 BETA

### New

- More Right To Left support. If the Vernacular language is right-to-left, PDF Booklets will be ordered so that pages are ordered back-to-front (see Settings:Book Making).
- New right-click menu on pages for removing and duplicating.
- Language picker: when picking a language and there are more than 2 countries, now says, .e.g. "4 countries".
- Language picker: when picking a language, you can now have major spelling differences and it will still find the language.

### Fixes
- Fixed "sticky" scrollbar on page template list and Art Of Reading gallery.
- Fixed problems with opening books over a network (needs user testing in various environments, though).
- Fixed problems with the front-cover language.
- Fixed problem with booklet pdfs.
- Fixed problem with downloading and then using book templates that are a collection of re-usable pages, e.g. Gleny's Water's Primer Template
- Book colors will now always stay the same (until we add the option of selecting the color you want).
- Many other small fixes

### Known Issues
- The gear-shaped button that brings up the Format box has positioning problems; we're working on a solution.

## 3.0.69 BETA (Windows Only)

- The Help menu now has has a "Report Problem" command.
- Now installs the newly updated Andika version 5.
- Fixed problem with "Booklet Insides" publishing option. 
- Fixed several issues in the Format dialog.
- Modifier keys no longer trigger the Special Characters popup.
- Several other minor fixes.

## 3.0.66 BETA (Windows Only)

### Flexible Layouts and Styling
- Use the new "Custom" page template to make your own unique page layouts.
- Use "Duplicate" button to reuse your custom page within a book.
- You can now set a default font family for each of the languages in a book.
- You can now apply styles name (like Word / LibreOffice) to text boxes keep things consistent throughout the book
- You can now set the font family, size, line spacing, word spacing, justification, border, and background of each text box, along with all other boxes with the same "style". Just click on the little "gear" icon in the lower left of a text box.

### Improved PDF Making
- Bloom now uses the same rendering engine for both editing and pdf-making, eliminating WYSIWYG glitches of previous versions.
- New PDF engine renders fonts better.
- Languages requiring Graphite Complex-non-roman script rendering are now supported.
- Adobe Acrobat no longer needed to view PDFs in the Publish tab

### Other
- Holding down a key now shows a "Special Characters Panel" that lets you select from similar characters. Use you mouse, mouse wheel, or arrow keys to select the character you want (see screenshot below).
- You can now right click on a book and export its contents to Word or Libre Office (most formatting will be lost).
- Bloom's interface has new translations, in Arabic, Chinese, Tamil (India), Hindi (India), Telugu (India), and Kinyarwanda (Rwanda). French and Spanish translations have been updated.
- Andika Font is now installed along with Bloom

###Experimental Features in this release
- Decodable Reader Tool helps you develop a series of books that introduce a few letters at a time.
	* Reads a folder for texts you have placed there, and suggests words to the writer that are "decodable" at each stage.
	* Words that are not appropriate for the current stage are highlighted.
	* You can export a file detailing each decodable stage: letters, sight words, and available words to use.
	* Thanks to Norbert Rennert for sharing code from his Synphony engine.
  * See "Help: Building/User Reader Templates" for more information.
- Leveled Reader Tool helps you develop books for readers at various levels of ability by setting limits on the number of words per sentence, page, and book.
  * See "Help: Building/User Reader Templates" for more information.
- New "Custom" page that lets you divide up the page into text and picture portions. You can then just use the page, or treat it like a template for other pages in the book.
- Holding down a key now shows a "Special Characters Panel" that lets you select from similar characters.
- Languages can be marked as Right-To-Left. However, changing the page order is still up to you, using a PDF editor like Adobe Acrobat (or maybe use the RTL option in PdfDroplet)?
- Initial Linux Version (Precise and Trusty).

### Known Bugs & Limitations
- "A5Portrait Bottom Align" does not layout correctly in bilingual or trilingual mode [BL-46].
- Page Template names are always shown in English.
- Books with Graphite complex-non-roman scripts cannot be printed directly from Bloom yet. Instead, open the PDF in Adobe Reader and print from there.
- Sample texts for use with the decodable reader must be saved as unicode text files.

## 2.0 RELEASE October 2014

## 2.0.1038 BETA 23 July 2014
A4Landscape with "Picture on top" now gives 70% of the page to the picture, was previously 45%.


## 2.0.1038 BETA 6 June 2014
Added ability to have <p></p> edit boxes, rather than FF's <BR /> default, so you can do styling like paragraph indents. Needed for SIL-LEAD SHRP project.

## 2.0 1022 BETA 13 May 2014
Templates can now have a markdown "readme" for telling people about the template. Select the Big Book or Wall Calendar templates to see how these are displayed.

## 2.0.1021 BETA 9 May 2014
Can now publish books to books.bloomlibrary.org
Can now get books at books.bloomlibrary.org and they will open in Bloom
New Big Book template & Front matter
Basic Book now auto-enlarges fonts if you make it A4Landscape (for making a5 books into Big Books)


## 1.1.574 18 Feb 2014
New experimental keyboard shortcuts:
Bold=Ctrl+b, Underline=Ctrl+u, Italics=Ctrl+i, F6=Superscript, Ctrl+space=clear any of those.
F7=Heading1, F8=Heading2.
Pasting text with \v 123 will give you a superscript 123.


## 1.1  12 Feb 2014
Make textboxes red when there is more text in them than fits

## 1.1.6 - 19 Dec 2013
* for template developers: <body> now has a class "publishmode" that you can use to do something different when viewing/publishing vs. editing
* make shift-insert act just like ctrl+v with respect to filtering the incoming content down to plain text.

## 1.1.5 - 13 Dec 2013
* You can now add additional usage limitation information to a Creative Commons license.
* [Template Development] JADE is now the standard way to make custom templates
* Fixed problem that prevented changing the collection name.

## 1.1.1 - 1 Nov 2013
* Fixed problem with picking the wrong version of xmatter stylesheets at runtime

## 1.0.29 - 3 Feb 2014
* Update French localization

## 1.0.28 - 31 Jan 2014
* Move format version of Bloom 1.0 up to 1.1. and start rejecting books that are greater than that.

## 1.0.25 - 31 Jan 2014
* Fix problem where unicode characters in project folder would make images not show up in PDF

## 1.0.24 - 13 Dec 2013
* Fix Problem of readonly thumbnails when updating a collection with a BloomPack.
* Fix height of green source text bubble on Just Text pages.
* Fix bug tht would cause problems if you used a '&' in a copyright notice.
* Add context menu item for getting stylesheet troubleshooting information to the clipboard.
* Avoid collecting new strings for localization from non-developer machines.
* Add "publish" class to the <body> when previewing or making a publish. Stylesheets can use
	this to do different things when editing vs. publishing.
* Fix problem of folio books not using the most recent stylesheet.

## 1.0.19 - 1 Nov 2013
* Fix Documentation Link from Collection Tab
* For template designers, offer a file of jade mixins

## 1.0.17 - 1 Nov 2013
* Background color on elements wasn't getting through to the PDF

## 1.0.16 - 31 Oct 2013
* Stop complaining if the user has restricted access to the localization files but isn't trying to localize anything

## 1.0.12 - 18 Oct 2013
* enabled pages to incorporate combobox that control layouts dynamically

## 1.0.09 - 14 Oct 2013
* added Spanish localizations
* dropped 'beta' label

## 1.0.08 - 10 Oct 2013
* now supports watermark hints on fields that haven't been filled in yet

## 1.0.06 - 12 Sept 2013
* Windows XP users trying to scan without the necessary windows components should now get a helpful message.

## 1.0.05 - 10 Sept 2013
* Fix style-sheet-related problem when showing Calendar pages

## 1.0.04 - 9 Sept 2013
* New right-click command on the collection to "Replace Missing Images", where you specify a folder to look for replacements.

## 1.0.03 - 6 Sept 2013
* If image file is missing, now shows a message and lets you chose another one.
* New right-click command on the collection to "Check All Books", which finds missing images.

## 0.9.117 29 Aug 2013
* Now prompts for registration starting on 3rd launch.

## 0.9.115 24 Aug 2013
* Added bloom-copyFromOtherLanguageIfNecessary to limit what fields get the copy treatment introduced in 0.9.114

## 0.9.114 24 Aug 2013
* Will now copy some fields from one global language (like English) to the national languages in use if it needs to in order to preserve information in derived books. For example, who the original illustrator was.
* Show "French" as "français" in settings dialog.
* Better error when there's a problem loading a book.
* Fixed forgetting the "experimental commands" setting
* Only show the copyright missing if the copyright itself is missing, not the license or illustrator.
* Don't show a * in the ISBN box
* When making new collection
 * automatically fill in country based on language chosen (will be wrong sometimes, but you see it and can change it).
 * If language is in PNG, auto set National Languages to English and Tok Pisin.
 * Book folders now take their names from vernacular if possible, rather than English. If the vernacular is empty, then the next choice is the National Language 1, then NL2, and only then English, French, Portuguese, or Thai.

## 0.9.111 17 Aug 2013
* New bloom-draggableLabel feature for when images need editable labels

## 0.9.110 17 Aug 2013
* Added missing null xmatter

## 0.9.109 14 Aug 2013
* Update french localizations
* Fix extra blank page problem

## 0.9.105 10 Aug 2013
* B5 pages turned into booklets now properly use B4 paper (were being squished onto B5 paper).

## 0.9.101 27 July 2013
* Allow xmatter to be installed via bloom packs (the creator/distributor of the bloom pack must manually add the xmatter pack to the zip file).

## 0.9.99 23 July 2013
* Collections now sort by "natural sort order" rather than alphabetic, so "Primer 3" comes before "Primer 21", and folio documents will include them in this order, too.

## 0.9.97 23 July 2013
 * Add Folio support, which is a book that sucks in the other books in the collection when it comes time to make a PDF
 * Add AllowNewBooks setting to collection, so that collections can be locked down
 * Add "xmatter" tag on books to select a different front/backmatter pack than the one set as the default for the collection
 * A book can now have a "bookTitleTemplate" so that the title (and thus file and folder name) can be automatically constructed based on fields in the book. E.g. Primer Term {term} Week {week}
 * Slight alteration in page zoom during PDF to address page breaks slowing moving upwards in really large documents.

## 0.9.96 16 July 2013
 * In Publish Tab, new option to turn on "Crop Marks" for commercial printing. In addition to the visible crop marks, there is a matching "TrimBox" for automated cropping.

## 0.9.95 13 July 2013
 * Fixed recent problem where custom font and styles were being ignored.

## 0.9.92 22 June 2013
 * New experimental "Export XML for InDesign" command on the right-click menu of books. Enable via the "Show experimental commands" setting in the Settings dialog.

## 0.9.91 20 June 2013

These changes will only be noticed by those of us writing new templates from scratch in html:

* When starting a book, now removes incoming content with lang = "x", so we can draft with "x" and know that it won't show to users.
* data-hints should now live on the enclosing bloom-translationGroup. On the minus side: for now, they will not dissappear even when there is content.

## 0.9.90 19 June 2013
* Alleviate problem (Windows 7) of sylesheets from old versions sticking around even when you upgrade (VirtualStore).

## 0.9.89 16 June 2013
* Fix page size/layout choices

## 0.9.86 10 June 2013
* Environment variable FEEDBACK set to "off" now disables analytics

## 0.9.83 10 June 2013
* Fix: Stylesheets references are now ordered when saved to disk (were previously ordered within bloom)
* Fix: Front/Back Mater (XMatter) css now saved with the book on disk (for a post-bloom future)
* Improve BloomPack Install responsiveness.

## 0.9.82 7 June 2013
* Fix: If you add an large image and then later crop it, you didn't see the crop on the page until you restarted Bloom.
* Fix: When using the auto-update, 2 copies of the new Bloom would be run.

## 0.9.80 5 June 2013
* When choosing a language, you can now customize how the name is actually displayed. You can also change it later, using the Configuration dialog.

## 0.9.79 4 June 2013
* Localization improvements:
 * Can now localize the column headers of the language chooser.
 * Many more "dynamic" strings available before you chance upon them.

## 0.9.78 4 June 2013
* Language Look Up now searches alternative names from Ethnologue.
* Language Look Up now lists countries
* Books now know the ids of their ancestor templates.
* Experimental InDesign XML Export (write to john if you're interested in helping experiment with this).
* Fixed bug that could cause lost localization strings during upgrade.

## 0.9.71 24 May 2013
* Improved PDF layout when OS is at 120dpi or 144dpi
* Allow BloomPack install even if Bloom is already running
* In the Language chooser, auto-select "Unlisted Language" if user clicks on the related link.
* Pretty-print html in the books

## 0.9.67 24 May 2013
* Updated French Localizations

## 0.9.66 23 May 2013
* Much faster startup time if you have lots of books
* Much better performance when editing book with many pages
* More accurate analytics

## 0.9.55 16 May 2013
* Fixed problem with error on machines lacking Adobe Reader
* Update French Localizations (Thx David Rowe)

## 0.9.54 15 May 2013
 * New "Picture & Word" page in Basic Book for simple one-word-per-page books.
 * A couple localization fixes.
 * Better message if user's machine doesn't know how to follow hyperlinks.

## 0.9.58 10 April 2013
 * New experimental B5 Primer template

## 0.9.53 2 April 2013
* Now Localizable
 * Settings Protection Dialog
 * Language chooser (shows in New Collection Wizard & Settings dialog)
* Now Mostly Localizable
 * Image ToolBox
 * Metadata editor

## 0.947 22 March 2013
* New Collection Wizard is now mostly localizable
* On first run, now detects user's OS Display Language and tries to use that, or offers other choices if it doesn't have that UI language yet.
* Parts of Bloom that come from outside libraries, so not yet localize-able:
 * Language chooser (shows in New Collection Wizard & Settings dialog)
 * Image Toolbox
 * Send/Receive
 * Tooltips in Publish Tab
 * Metadata Dialogs
 * Settings Protection Dialog

## 0.946 21 March 2013
* Add enough space to show French labels, where previously things were cut off.
* Make UI Language Chooser no longer experimental, so it's now on by default.
* Newly localize-able:
 * Page Size/Orientation choices
 * Page thumbnail names (e.g. "Title Page", "Just Text")
 * Open/Create Collection Dialog


## 0.945 20 March 2013
* Reorganized and cleaned up internationalization. Some more work is yet to be done.

## 0.944: 18 March 2013
* Initial French Localization (Thanks David Rowe)

## 0.940: 11 March 2013
* Bloom now checks for the availability of a new version and offers to download it for you

## 0.933: 5 March 2013
* Fix problem of new title not being reflected in the thumbnail until restart
* Improved problem of occasional cursor disappearance after coming in from another application.

## 0.9: 21 December 2012
* Small bug fixes
* No longer times out after 90 days, but it does prompt you to upgrade

## 0.9: 13 October 2012
* Partial Send/Receive support available as an opt-in experimental feature
* Partial Localization support available as an opt-in experimental feature
* Calendar, Picture Dictionary, and Template Maker are now an opt-in experimental feature


## 0.9: Sept 28 2012

* Can now change collection name in the Settings Dialog
* Can now set the default font in the Settings Dialog

## 0.9: Sept 22 2012

* Auto compression of pngs as you select them
* "Update All Books" menu command which updates frontmatter, illustration metadata, and compresses all imags
* Can now paste if there is an image path on the clipboard


## 0.9: Sept 20 2012

* Basic, experimental Send/Receive.

## 0.8: Sept 15 2012

* Pages are auto-zoomed on small screens, so that their full with always shows
* In the publish tab, you can now choose a different page layout (size/orientation)
* In the publish tab, the layout options now include an "A4Landscape Split Across Pages" style option, for making books with large images and text, designed to be read to others

## 0.8: July 16 2012

* To cope with large print-resolution images, Bloom now runs all images through a resolution-shrinking service before displaying them during editing. The full size originals are still used when making the PDF for printing.

## 0.8: July 4 2012

* You can now change images, after a warning, even on books that came from shells. If the image is placeholder (flower), you don't see the warning.
* changed former "missing" label to indicate that the image may also just not have loaded quickly enough
* upgraded to a newer pdfsharp in hopes of helping with a "missing token" error that was reported

## 0.8: June 28 2012

* Bloom is now totally tolerant of missing or messed up Adobe Reader. You just see a message and get reduced functionality.

## 0.8: June 27 2012

* The "image on top" page renamed to "Basic Text and Image"
* The "Basic Text and Image" page, in a5portrait & bi/tri-lingual now places vernacular above the picture

## 0.8: June 26 2012

* Collection folders now sport a "collection.css" file which can be used if necessary to override defaults. This isn't user-friendly by any means, but is a stop-gap measure.
* Similarly, if you place a "book.css" in the folder of the book itself, you can now override styles for just that one book.
* XMatter templates can now have back matter (in addition to the previous front-matter). Just use the class "bloom-backMatter"
* Factor XMatter now makes the back cover, both inside and outside, usable for text.

## 0.8: June 25 2012

* From-scatch overhaul of the First Time and Create Collection experiences. Note that the wizard with different questions depending on whether you choose Vernacular or Source
* Template stylesheets can now specify not just size/orientations they support, but also options which the user can select. The Basic Book now uses this to offer 2 different A4Landscape layouts.

## 0.8: May 14 2012

* Added experimental custom page to Basic Book, with toolbox for adding text and
		images

## 0.8: May 9 2012

* Control-mousewheel now does page zooming
   * Factory FrontMatter now puts the "Credits" page to the inside front cover, replacing the "verso" page
   * Factory FrontMatter now starts logical numbering with the title page as page 1. Doesn't show it, though.
   * New margin model
   * Margins are now sensitive to the odd/even'nes of the page

## 0.8: May 3 2012
* Control-mousewheel now does page zooming

## 0.7: April 13 2012

* BloomPacks.  To distribute collections of ShellBooks, you can now zip the collection, then change it from .zip to .BloomPack.  Take the BloomPack to another user's computer, and double-click it install that pack on their computer.

## 0.6: Feb 10 2012
### Format Changes
These are the last <em>planned</em> format changes before version 1.

* Stylesheets can now support multiple Page Size & Orientations.  A dummy css rule now tells Bloom which size/orientations the sheet supports.  See the format documentation for details.
* Start of a feature for pasting images via the clipboard: hover over the image and click the paste button. Needs some work and testing.




* Introduced "BasicBook" which replaces "A5Portrait Template". "BasicBook" currently supports A5Portrait and A4Landscape.
* Library tab:Double-clicking on a book takes you to the edit mode.
* Library tab: the right-click menu has several new useful commands.
* Edit tab: the toolbar now has a menu for changing the page size and orientation.
* Edit tab, Image Toolbox: several usability improvements.
	* Publish tab: new "Save" button suggests a name which indicates the language and the book portion that was saved to the PDF (e.g. cover, insides, etc.)
* Bubbles only show when the field is empty, or in focus. This makes it easier to see what is left to be done.


## 0.5: Jan 27 2012
### Format Changes

* The div which contains metadata used to be identified with class "-bloom-dataDiv". It is now identified by id, with id='bloomDataDiv'
* The class "imageHolder" is now "bloom-imageContainer"
* All stylesheet classes that used to start with "-bloom" now start with "bloom"
* You can now enter an ISBN #. This number is automatically removed when the book is used as a shell for a new book.
* Although jpegs should be rare in Bloom books (used only for photos), they are now left as jpegs, rather than being converted to PNGs. All other image formats are still converted to PNG.
* User can now make books bilingual or trilingual by ticking selecting one of the national languages.
* A Picture Dictionary template is now available, with resizable and draggable frames. The stylesheet adapts to monolingual, bilingual, or trilingual needs.
* All images contained within a div with class "bloom-imageHolder" are now proportionally resized and centered within the containing div
* Divs with the class "bloom-draggable" can be moved by the user
* Divs with the class "bloom-resizable" can be resized the user
* The standard "A5Portrait" template now uses html div's instead of textarea's, to facilitate future internal formatting commands

### Known Problems

* page thumbnails don't always update.
  * li translations bubble covers "book li in ___".
  * Adding images leaves an unneeded "_orginal" copy.
  * Custom license can't have single quotes (appostrophes)
  * Once a CreativeCommons license is chosen, changing to another option leaves us with the CC license Image.
  * In bi/trilingual books, you can't yet change order of the languages.


## 0.4: Jan 2011
### Known Problems

* <del>Reported installation problems, related to "libtidy.dll"</del>
* Calendar layout should not display new template pages.
* Book cover Thumbnails aren&#39;t updating.
* Spell checking ignores the actual language (will be fixed when we upgrade to
		FireFox 9)
* <del>Source Text bubbles should not be editable</del>
* <del>Book previews should not be editable</del>
* <del>Copyright needs help formatting the year and copyright symbol</del>
* If you change any of the language settings, you must quit and re-run Bloom (or
		stay in Bloom but re-open the project).
* <del>After a long period of inactivity, a javascript error may be reported. This does
		no harm.</del>
* <del>The user interface of publish tab is somewhat at the mercy of you Adobe Acrobat
		installation (which version, how it is configured).</del>
* Many small page layout problems, for example pictures too close to the margin. Final layout issues are easy to fix but not a priority at the moment.


* Copyright/license dialog now has separate "year" field, and auto-generates the "Copyright ©" portion.
* User Settings Dialog:
* Vernacular Language
* National Language
* Regional or secondary national language
* Province
* District
* Front Matter Pack

* Country/Organization-specific Front Matter Packs. See documentation.
* Hint Bubbles for metadata fields
* Source Text in Bubbles with tabs for all the source languages
* Completely re-written format documentation
* Changed how we embed Adobe Acrobat Reader in order to reduce complexity for the non-tech user. Tested with Adobe Reader 10 and Acrobat 7.1
* Topic Chooser
	* Books and images can now have custom prose licenses


## 0.3: Dec 2011
### Format Changes
Style name change: coverBottomBookKind --&gt; coverBottomBookTopic
Shells and templates may no longer include front or back matter pages.

Introduced Factory-XMatter.htm and accompanying stylesheet.&nbsp; The contents
of this are now inserted into each new book.&nbsp; These templates, which will
be replaced by organizations/countries for their own needs, are populated by
data from a combination of sources:


* &nbsp;Data from the library: language name, province, district, etc.
* &nbsp;Data from the &quot;-bloomDataPage&quot; div of the shellbook itself. E.g.,
		acknowledgments, copyright, license.
	<liData from the user. E.g. Translator name.

### &nbsp;UI improvements:

* First editable text box is now automatically focused
* The currently focused text box is now highlighted with a colored border.
  elements with &quot;data-hint=&#39;tell the user something&quot; now create a nice speech-bubble on the right

## 0.3: 9 Dec 2011
Format change: id attributes are no longer used in textareas or img's.
'hideme' class is no longer used to hide elements in languages other than those in the current publication. Instead, hiding is now the reponsibility of "languageDisplay.css", still based on the @lang attributes of elements.
	 evelopers can now right-click on pages and choose "View in System Browser". You can then have access to every bit of info you could want about the html, stylesheets, & scripts, using firefug (Firefox), or Chrome developer tools.

## 0.3: 1 Dec 2011
###Breaking Changes
I've abandoned the attempt to store Bloom files as xml html5.&nbsp; I risked death by a thousand paper cuts.&nbsp; So, as of this version, Bloom uses strictly-valid HTML5 (whether well-formed-xml or not).&nbsp; Internally, it's still doing xml; it uses HTML Tidy to convert to xml as needed.&nbsp; Other validation issues: The header material has changed so that it now passes the w3c validation for html5, with the only remaning complaint being the proprietary &lt;meta&gt; tags. &lt;meta&gt; tags now use @name attributes, instead of @id attributes.&nbsp; See the updated File Format help topic for updated information.

## 0.3: 30 Nov 2011
Making use of a new capabilty offered by html5, many formerly "special" classes have been moved to div-* attributes:

## 0.3: 29 Nov 2011
####Breaking Changes
Moved collections locations to c:\programdata\sil\bloom\collections. Formerly, the sil part was missing. Bloom
now creates this folder automatically. Now avoiding the word &quot;project&quot;, in favor
of &quot;library&quot;. So now you &quot;create a library for your language&quot;. This change also
shows up in configuration files, so if anyone has an existing .BloomLibrary
file, throw away the whole folder.

## 0.3: 25 Nov 2011
A5 Wall Calendar now usable, with vernacular days and months. Could use a graphic designer's love, though.

## 0.3: 24 Nov 2011
In the image toolbox, you can now reuse metadata (license, copyright, illustrator) from the last image you edited.

