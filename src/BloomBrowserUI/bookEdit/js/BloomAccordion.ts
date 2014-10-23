/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/jquery-ui.d.ts" />

class BloomAccordion {

  constructor() {
    $("#accordion").accordion({
      heightStyle: "fill"
    });
  }

  static Resize() {
    $("#accordion").accordion("refresh");
    //var myHeight = $(document).find(".accordionRoot").innerHeight();
    //console.log("Refreshed accordion to: "+myHeight);
  }
}