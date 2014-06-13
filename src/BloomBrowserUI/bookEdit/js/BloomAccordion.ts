/// <reference path="../../lib/jquery.d.ts" />

interface accordionInterface extends JQuery {
	accordion(options: any): JQuery;
}

class BloomAccordion {

	constructor() {
		(<accordionInterface>$("#accordion")).accordion({
			heightStyle: "fill"
		});
	}

	static Resize() {
		(<accordionInterface>$("#accordion")).accordion("refresh");
		//var myHeight = $(document).find(".readerToolsRoot").innerHeight();
		//console.log("Refreshed accordion to: "+myHeight);
	}
}