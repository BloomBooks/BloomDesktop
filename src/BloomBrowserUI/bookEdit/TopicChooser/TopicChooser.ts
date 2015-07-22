/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/jquery-ui.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="../../lib/jquery.i18n.custom.ts" />


// This must not be renamed. It s called directly from Bloom via RunJavaScript()
// ReSharper disable once InconsistentNaming
var ShowTopicChooser = () => {
    TopicChooser.showTopicChooser();
}


class TopicChooser {
    static showTopicChooser() {
        var currentTopicKey = $("div[data-book='topic']").parent().find("[lang='en']").text();

        TopicChooser.createTopicDialogDiv(currentTopicKey);
        var dlg = <any> $("#topicChooser").dialog({
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
                            var translationGroup = $("div[data-book='topic']").parent();
                            var englishDiv = translationGroup.find("[lang='en']")[0];
                            if (!englishDiv) {
                                englishDiv = translationGroup.find("div[data-book='topic']")[0];
                                $(englishDiv).clone().attr("lang", "en");
                                $(englishDiv).appendTo($(translationGroup));
                            }

                            //for translation convenience, we use "NoTopic" as the key during UI. But when storing, it's cleaner to just save empty string if we don't have a topic
                            if (key == "NoTopic") {
                                 //this will clear all of them, for everylanguage
                                 $("div[data-book='topic']").text("");
                            } else {
                                $(englishDiv).text(key);

                                //NB: when the nationalLanguage1 is also English, this won't do anything, but that's ok because we set the element to the key which is the same as its
                                //English, at least today. If that changes in the future, we'd need to put the "key" somewhere other than in the text of the English element
                                localizationManager.asyncGetTextInLang("Topics." + key, key, "N1")
                                    .done(topicInNatLang1 => {
                                        $("div[data-book='topic']").filter(".bloom-contentNational1").text(topicInNatLang1);
                                    });
                            }
                        }
                        $(this).dialog("close");
                    }
                }
            }
        });

        //make a double click on an item close the dialog
        dlg.find("li").dblclick(() => {
            var x = dlg.dialog("option", "buttons");
            x['OK'].apply(dlg);
        });
    }

    static createTopicDialogDiv(currentTopicKey: string) {
        // if it's already there, remove it
        $("#topicChooser").remove();

        //Make us a div with an empty list of topics. The caller will have to turn this into an actual dialog box.

        $("<div id='topicChooser' title='Topics'>" +
            "<style scoped>" +
            "           @import '/bloom/bookEdit/TopicChooser/topicChooser.css'" +
            "   </style>" +
            "<ol id='topicList'></ol></div>").appendTo($("body"));

        //now fill in the topic
        this.populateTopics(currentTopicKey);
    }

    static populateTopics(currentTopicKey: string) {
        var iframeChannel = getIframeChannel();


        iframeChannel.simpleAjaxGet('/bloom/topics', topics => {
            // Here, topics will be an object with a property for each known topic. Each property is a key:value pair
            // where the key is the English, and the value is the topic in the UI Language

            // add all the other topics, selecting the one that matches the currentTopic, if any
            for (var i in Object.keys(topics)) {
                var key = Object.keys(topics)[i];
                var extraClass = (key === currentTopicKey) ? " ui-selected " : "";
                $("ol#topicList").append("<li class='ui-widget-content " + extraClass + " ' data-key='" + key + "'>" + topics[key] + "</li>");
            }

            $("#topicList").dblclick(() => {
                $("#OKButton").click();
            });

            //This weird stuff is to make up for the jquery uI not automatically theme-ing... without the following,
            //when you select an item, nothing visible happens (from stackoverflow)
            $("#topicList").selectable({

                cancel: '.ui-selected', //allows double-clicking work

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
            $("#topicList li").hover(
                function () {
                    $(this).addClass('ui-state-hover');
                },
                function () {
                    $(this).removeClass('ui-state-hover');
                });
        });
    }
}