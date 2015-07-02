/// <reference path="../../bookEdit/js/bloomSourceBubbles.ts" />
/// <reference path="../../lib/jasmine/jasmine.d.ts"/>

"use strict";

describe("bloomSourceBubbles", function () {
  // reset fixture
  beforeEach(function () {
    $('body').html('');
  });

  it("Run MakeSourceTextDivForGroup with pre-defined settings", function () {
    // TODO: Testing is a bit hampered by not being able (currently) to put test values
    // into the cSharpDependencyInjector version of GetSettings(). Someday it might
    // be worth modifying that file so that tests can setup their own values for:
    // defaultSourceLanguage ('en' in tests; also marked vernacular)
    // currentCollectionLanguage2 ('tpi' in tests)
    // currentCollectionLanguage3 ('fr' in tests)

    var testHtml = $([
      "<div id='testTarget' class='bloom-translationGroup'>",
      "   <div class='bloom-editable' lang='es'>Spanish text</div>",
      "   <div class='bloom-editable' lang='en'>English text</div>",
      "   <div class='bloom-editable' lang='fr'>French text</div>",
      "   <div class='bloom-editable' lang='tpi'>Tok Pisin text</div>",
      "</div>"
    ].join("\n"));
    $('body').append(testHtml);
    var result = bloomSourceBubbles.MakeSourceTextDivForGroup($('body').find('#testTarget')[0]);
    var listItems = result.find('nav ul li');
    expect(listItems.length).toBe(3); // English in test is vernacular, so no tab for it
    // Tok Pisin tab gets moved to first place, since it is currentCollectionLanguage2
    // French is second, since it is currentCollectionLanguage3
    expect(listItems.first().html()).toBe("<a class=\"sourceTextTab\" href=\"#tpi\">Tok Pisin</a>");
    expect(result.find('li#fr').html()).toBe("<a class=\"sourceTextTab\" href=\"#fr\">français</a>");
    expect(listItems.last().html()).toBe("<a class=\"sourceTextTab\" href=\"#es\">español</a>");
    expect(result.find('div').length).toBe(4); // including English
  });

  it("Run CreateDropdownIfNecessary with pre-defined settings", function () {
    // TODO: Testing is a bit hampered by not being able (currently) to put test values
    // into the cSharpDependencyInjector version of GetSettings(). Someday it might
    // be worth modifying that file so that tests can setup their own values for:
    // defaultSourceLanguage ('en' in tests; also marked vernacular)
    // currentCollectionLanguage2 ('tpi' in tests)
    // currentCollectionLanguage3 ('fr' in tests)

    var testHtml = $([
      "<div id='testTarget' class='bloom-translationGroup'>",
      "   <nav>",
      "     <ul>",
      "       <li id='tpi'><a class='sourceTextTab' href='#tpi'>Tok Pisin</a></li>",
      "       <li id='fr'><a class='sourceTextTab' href='#fr'>français</a></li>",
      "       <li id='es'><a class='sourceTextTab' href='#es'>español</a></li>",
      "    </ul>",
      "   </nav>",
      "   <div class='source-text' lang='es'>Spanish text</div>",
      "   <div class='source-text' lang='fr'>French text</div>",
      "   <div class='source-text' lang='tpi'>Tok Pisin text</div>",
      "</div>"
    ].join("\n"));
    $('body').append(testHtml);
    var result = bloomSourceBubbles.CreateDropdownIfNecessary($('body').find('#testTarget'));
    var listItems = result.find('nav ul li');
    expect(listItems.length).toBe(3); // English in test is vernacular, so no tab for it
    // Tok Pisin tab gets moved to first place, since it is currentCollectionLanguage2
    expect(listItems.first().html()).toBe("<a class=\"sourceTextTab\" href=\"#tpi\">Tok Pisin</a>");
    var frenchTab = result.find('li#fr');
    expect(frenchTab.html()).toBe("<a class=\"sourceTextTab\" href=\"#fr\">français</a>");
    expect(frenchTab.attr('style')).toBeUndefined(); // 2nd visible tab
    var spanishTab = listItems.last();
    expect(spanishTab.html()).toBe("<a class=\"sourceTextTab\" href=\"#es\">español</a>");
    expect(spanishTab.attr('style')).toBe("display: none;"); // 3rd tab should be in dropdown
    expect(result.find('.styled-select-overlay').length).toBe(1); // empty overlay div
    var options = result.find('.styled-select option');
    expect(options.length).toBe(2); // including selected empty option
    expect(options.first().html()).toBe("español");
    expect(options.last().html()).toBe("");
    var ulChildren = result.find('nav ul').children();
    expect(ulChildren.length).toBe(5); // including div and select for dropdown
  });
});
