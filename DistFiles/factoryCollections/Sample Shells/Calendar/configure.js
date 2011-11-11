// Copyright 2011 SIL International.  License: Academic Free License

/**
* @fileoverview Creates calendar pages for a Bloom book.
*/

/**
* Updates the dom to reflect the given configuration settings
* Called directly by Bloom, in a context where the current dom is the book.
* @param {configuration} members come from the name attributes of the corresponding configuration.htm file.
*       Put a new input control in that file, give it a @name attribute, and the value will be available here.
*/
function updateDom(configuration) {
    var year = Number.from(configuration.calendar.year);
    var previous = $$('.titlePage')[0];
    onClick = "window.alert('alert!')"
    for (var month = 0; month < 12; month++) {
        var monthElement = generateMonth(year, month);
        monthElement.inject(previous, "after");
        previous = monthElement;
    }
}

function generateMonth(year, month) {
  var monthPage = new Element("div", {
      "class": "-bloom-page -bloom-required month"
      });
  new CalConf(monthPage).draw(year, month);
  return monthPage;
}

var CalConf = new Class({
    Implements: [Options],

    options: {},

    initialize: function(wrapper, options) {
        this.wrapper = wrapper;
        this.setOptions(options);
        this.dayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        this.ndays = this.dayNames.length;
        this.monthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    },

    draw: function(year, month) {
        this.table = new Element("table");
        this.drawHeader(year, month);
        this.drawBody(year, month);
        this.table.inject(this.wrapper);
    },

    drawHeader: function(year, month) {
        var thead = new Element("thead");
        var row = new Element("tr");
        var mname = new Element("td", {
            colspan: this.ndays
        }).set("text", this.monthNames[month]).inject(row);
        row.inject(thead);
        row = new Element("tr");
        this.dayNames.each(function(n) {
            new Element("th").set("text", n).inject(row);
        });
        row.inject(thead);
        thead.inject(this.table);
    },

    drawBody: function(year, month) {
        var body = new Element("tbody");
        var start = new Date(year, month, 1);
        start.setDate(1 - start.getDay());
        do {
            start = this.drawWeek(body, start, month);
        } while (start.getMonth() == month);
        body.inject(this.table);
    },

    drawWeek: function(body, date, month) {
        var row = new Element("tr");
        for (var i = 0; i < 7; i++) {
            var day = new Element("td");
            if (date.getMonth() == month) {
                day.set("text", date.getDate());
            }
            day.inject(row);
            date.increment();
        }
        row.inject(body);
        return date;
    }
});
