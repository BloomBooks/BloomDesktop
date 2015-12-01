/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/jquery-ui.d.ts" />

class BloomToolbox {

  constructor() {
    $("#toolbox").accordion({
      heightStyle: "fill"
    });
  }

  static Resize() {
    $("#toolbox").accordion("refresh");
    //var myHeight = $(document).find(".toolboxRoot").innerHeight();
    //console.log("Refreshed toolbox to: "+myHeight);
  }
}