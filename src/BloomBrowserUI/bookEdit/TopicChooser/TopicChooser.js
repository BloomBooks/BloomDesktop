/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/jquery-ui.d.ts" />
/// <reference path="../../lib/localizationManager.ts" />
/// <reference path="../../lib/jquery.i18n.custom.ts" />
/// <reference path="../js/getIframeChannel.ts"/>
/// <reference path="../js/interIframeChannel.ts"/>
// This must not be renamed. It s called directly from Bloom via RunJavaScript()
// ReSharper disable once InconsistentNaming
var ShowTopicChooser = function () {
    TopicChooser.showTopicChooser();
};

var TopicChooser = (function () {
    function TopicChooser() {
    }
    TopicChooser.showTopicChooser = function () {
        localizationManager.asyncGetTextInLang("Topics.Health", "--{0}--", "N1", "blah").done(function (s) {
            alert(s);
        }).fail(alert('failed'));
        TopicChooser.createTopicDialogDiv();
        var dlg = $("#topicChooser").dialog({
            autoOpen: true,
            modal: true,
            position: {
                my: 'top',
                at: 'top',
                of: $('.bloom-page')
            },
            buttons: {
                "OK": {
                    id: "OKButton",
                    text: "OK",
                    width: 100,
                    click: function () {
                        var t = $("ol#topicList li.ui-selected");

                        //set or clear the topic variable in our data-div
                        if (t.length) {
                            var key = t[0].dataset['key'];

                            //ignore the visible editable for now, set the key into the English (which may be visible editable, but needn't be)
                            $("div[data-book='topic']").parent().find("[lang='en']").remove();
                            $("div[data-book='topic']").parent().append('<div data-book="topic" class="bloom-readOnlyInTranslationMode bloom-editable" contenteditable="true" lang="en">' + key + '</div>');

                            //var topicInNatLang1 = localizationManager.getTextInLanguage("Topics." + key, englishText, "lang2");
                            localizationManager.asyncGetTextInLang("Topics." + key, key, "N1").done(function (topicInNatLang1) {
                                $("div[data-book='topic']").filter("[class~='bloom-contentNational1']").text(topicInNatLang1);
                            });
                        }
                        $(this).dialog("close");
                    }
                }
            }
        });

        //make a double click on an item close the dialog
        dlg.find("li").dblclick(function () {
            var x = dlg.dialog("option", "buttons");
            x['OK'].apply(dlg);
        });
    };
    TopicChooser.populateTopics = function () {
        var iframeChannel = getIframeChannel();

        iframeChannel.simpleAjaxGet('/bloom/topics', function (topics) {
            $("ol#topicList").append("<li class='ui-widget-content' data-key=''>(" + topics.NoTopic + ")</li>");
            for (var i in topics.pairs) {
                $("ol#topicList").append("<li class='ui-widget-content' data-key='" + topics.pairs[i].k + "'>" + topics.pairs[i].v + "</li>");
            }

            $("#topicList").dblclick(function () {
                $("#OKButton").click();
            });

            //This weird stuff is to make up for the jquery uI not automatically theme-ing... without the following,
            //when you select an item, nothing visible happens (from stackoverflow)
            $("#topicList").selectable({
                cancel: '.ui-selected',
                unselected: function () {
                    $(":not(.ui-selected)", this).each(function () {
                        $(this).removeClass('ui-state-highlight');
                    });
                },
                selected: function () {
                    $(".ui-selected", this).each(function () {
                        $(this).addClass('ui-state-highlight');
                    });
                }
            });
            $("#topicList li").hover(function () {
                $(this).addClass('ui-state-hover');
            }, function () {
                $(this).removeClass('ui-state-hover');
            });
        });
    };

    TopicChooser.createTopicDialogDiv = function () {
        $("#topicChooser").remove();
        $("<div id='topicChooser' title='Topics'>" + "<style scoped>" + "           @import '/bloom/bookEdit/TopicChooser/topicChooser.css'" + "   </style>" + "<ol id='topicList'></ol></div>").appendTo($("body"));
        this.populateTopics();
    };
    return TopicChooser;
})();
//# sourceMappingURL=TopicChooser.js.map
