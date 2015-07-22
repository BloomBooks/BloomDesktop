//
// Typescript configuration file for Bloom Wall Calendar
//
// Creates calendar pages for a Bloom book.
//
//   This script is fed a configuration object as a JSON string with the following elements:
//       configuration.calendar.year
//
//       These ones are in the "library" zone because they will be reused in future years/other calendar types
//       configuration.library.calendar.monthNames[0..11]
//       configuration.library.calendar.dayAbbreviations[0..6]
//
//   This script relies on the 2 pages that should be in the DOM it operates on:
//       One with classes 'calendarMonthTop'
//       One with classes 'calendarMonthBottom'
//
/// <reference path="jquery.d.ts" />

//
// This is the main public entry point called by Configurator.ConfigureBookInternal()
// in a context where the current dom is the book.
//
function runUpdate(jsonConfig:string) {
    var configurator = new CalendarConfigurator(jsonConfig);
    configurator.updateDom();
}

//
// Used to create a calendar in a Wall Calendar book
//
class CalendarConfigurator {

    private configObject : CalendarConfigObject;

    constructor(jsonConfig:any) {
        if (jsonConfig && jsonConfig['library'])
            this.configObject = new CalendarConfigObject(jsonConfig);
        else
            this.configObject = new CalendarConfigObject(); // shouldn't happen, even in test...
    }
    //
    // Updates the dom to reflect the given configuration settings
    //
    updateDom():void {
        var year:string = this.configObject.year;
        var originalMonthsPicturePage:HTMLElement = $('.calendarMonthTop')[0];
        var pageToInsertAfter:HTMLElement = originalMonthsPicturePage;
        for (var month:number = 0; month < 12; month++) {

            var monthsPicturePage:HTMLElement = $(originalMonthsPicturePage).clone()[0];
            $(monthsPicturePage).removeClass('templateOnly').removeAttr('id'); // don't want to copy a Guid!

            $(pageToInsertAfter).after(monthsPicturePage);

            var monthDaysPage:HTMLDivElement = this.generateMonth(year, month, this.configObject.monthNames[month], this.configObject.dayAbbreviations);
            $(monthsPicturePage).after(monthDaysPage);
            pageToInsertAfter = monthDaysPage;
        }
        $('.templateOnly').remove(); // removes 2 template pages (calendarMonthTop and calendarMonthBottom)
    }

    generateMonth(year: string, month: number, monthName: string, dayAbbreviations: string[]):HTMLDivElement {
        var marginBox:HTMLDivElement = document.createElement('div');
        $(marginBox).addClass('marginBox');

        var monthPage:HTMLDivElement = document.createElement('div');
        $(monthPage).addClass('bloom-page bloom-required A5Landscape calendarMonthBottom');
        $(monthPage).attr('data-page', 'required');
        this.buildCalendarBottomPage(marginBox, year, month, monthName, dayAbbreviations);
        monthPage.appendChild(marginBox);
        return monthPage;
    }

    buildCalendarBottomPage(bottomPageContainer : HTMLDivElement, year : string, month : number, monthName : string, dayAbbreviations : string[]):void {
        var header:HTMLParagraphElement = document.createElement('p');
        $(header).addClass('calendarBottomPageHeader');
        $(header).text(monthName + " " + year);
        $(bottomPageContainer).append(header);
        var table:HTMLTableElement = document.createElement('table');
        this.buildCalendarHeader(table, dayAbbreviations);
        this.buildCalendarBody(table, year, month);
        $(bottomPageContainer).append(table);
    }

    buildCalendarHeader(containingTable : HTMLTableElement, dayAbbreviations : string[]) {
        var thead:HTMLTableSectionElement = document.createElement('thead');
        var row:HTMLTableRowElement = document.createElement('tr');
        dayAbbreviations.forEach(function (abbr) {
            var thElem:HTMLTableHeaderCellElement = document.createElement('th');
            $(thElem).text(abbr);
            $(row).append(thElem);
        });
        $(thead).append(row);
        $(containingTable).append(thead);
    }

    buildCalendarBody(containingTable : HTMLTableElement, year:string, month:number): void {
        var body:HTMLTableSectionElement = document.createElement('tbody');
        var start = new Date(parseInt(year), month, 1);
        start.setDate(1 - start.getDay());
        do {
            start = this.buildWeek(body, start, month);
        } while (start.getMonth() == month);
        $(containingTable).append(body);
    }

    buildWeek(body, date, month): Date {
        var row:HTMLTableRowElement = document.createElement('tr');
        for (var i:number = 0; i < 7; i++) {
            var dayCell:HTMLTableDataCellElement = document.createElement('td');
            if (date.getMonth() == month) {
                var dayNumberElement:HTMLParagraphElement = document.createElement('p');
                $(dayNumberElement).text(date.getDate());
                $(dayCell).append(dayNumberElement);
                var dayCellTextArea:HTMLTextAreaElement = document.createElement('textarea');
                $(dayCell).append(dayCellTextArea);
            }
            $(row).append(dayCell);
            date.setDate(date.getDate() + 1);
        }
        $(body).append(row);
        return date;
    }
}

class CalendarConfigObject {
    private defaultConfig:any = {
        "calendar": {"year": "2015"},
        "library": {
            "calendar": {
                "monthNames": ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"],
                "dayAbbreviations": ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]
            }
        }
    };

    public year : string;
    public monthNames : string[];
    public dayAbbreviations : string[];

    constructor (jsonConfig? : any) {
        if (typeof jsonConfig === "undefined" || !jsonConfig['library'])
            jsonConfig = this.defaultConfig;
        this.year = jsonConfig.calendar.year;
        this.monthNames = jsonConfig.library.calendar.monthNames;
        this.dayAbbreviations = jsonConfig.library.calendar.dayAbbreviations;
    }
}

// Test class for manual debugging
// Just load DistFiles\factoryCollections\Templates\Wall Calendar\Wall Calendar.htm into Firefox
// and type "TestCalendar()" in Firefox's Console tab to debug
class TestCalendar {
    // test config is in French, just for fun
    private testConfig : string =  '{"calendar": { "year": "2015" },' +
        '"library":  {"calendar": {' +
        '"monthNames": ["janv", "fév", "mars", "avr", "mai", "juin", "juil", "aôut", "sept", "oct", "nov", "déc"],' +
        '"dayAbbreviations": ["Dim", "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam"]}}}';
    constructor() {
        var configurator = new CalendarConfigurator(this.testConfig);
        configurator.updateDom();
    }
}