/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../typings/jqueryui/jqueryui.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="../../lib/jquery.i18n.custom.ts" />
import axios = require('axios');

// This must not be renamed. It s called directly from Bloom via RunJavaScript()
// ReSharper disable once InconsistentNaming
var ShowTopicChooser = () => {
    TopicChooser.showTopicChooser();
}


export default class TopicChooser {
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
            buttons: [
                {
                    id: "OKButton",
                    text: "OK",
                    width: 100,
                    click() {
                        var t = $("ol#topicList li.ui-selected");
                        //Ask the Model to set or clear the topic variable
                        if (t.length) {
                            var key = t[0].dataset['key'];
                            //notice here that we are not changing the topic on the page.
                            //Doing so here would mean duplicating the logic we have to have
                            //in c# anyhow. Instead, we are doing more of a react-style
                            //thing here where the UI raises an event requesting a change
                            //to the model, and then let that propagate back down to the
                            //ui (that is, the html on the page).
                            TopicChooser.fireCSharpEvent('setTopic', key);
                        }
                        $(this).dialog("close");
                    }
                }
            ]
        });

        //make a double click on an item close the dialog
        dlg.find("li").dblclick(() => {
            var x = dlg.dialog("option", "buttons");
            x['OK'].apply(dlg);
        });
    }

    static fireCSharpEvent(eventName, eventData): void {
        var event = new MessageEvent(eventName, { 'bubbles': true, 'cancelable': true, 'data': eventData });
        top.document.dispatchEvent(event);
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
        axios.get<any>('/bloom/topics').then(result => {
            var topics = result.data;
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