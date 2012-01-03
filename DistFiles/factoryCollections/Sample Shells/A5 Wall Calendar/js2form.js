/*
 * requires:
 * underscore.js
 * jQuery.js
 */
var populateForm =
    (function (){
   function getFromPath(obj,travel){
       //walks a path defined by an array of fields, returns the last field walked
       if(_.isEmpty(travel)){
     return obj;
       }
       if(!obj){
     return null;
       }
       var prop = _.first(travel);
       return getFromPath(obj[prop],_.rest(travel));
   };
   function assignFromPath(obj,travel,assignVal){
       //walks a path defined by an array of fields, assigns a value to the last field walked
       if(_.isEmpty(travel)){
     obj = assignVal;
     return obj;
       }
       if(!obj){
     return null;
       }
       var prop = _.first(travel);
       obj[prop] = assignFromPath(obj[prop],_.rest(travel),assignVal);
       return obj;
   };
   function jsPather(pathStr){
       //converts js obj notation into a path array
      return pathStr
     .replace(/\[/g,'.')
     .replace(/\]/g,'')
     .split(".");
   };
   function nodesProcessor(obj,$nodes,varAtt,callback,pather){
       /*changes the shape of the obj to suit the form
        *e.g. {i : ["1","2","3"]} -> {i:"123"}
        */
       if(_.isUndefined(pather)){
     var pathTranslator = jsPather;
       }
       else{
     var pathTranslator = pather;
       }
       $($nodes).each(function(){
      var varType = $(this).attr('var_type');
      var nodeName = $(this).attr(varAtt);
      var varPath = pathTranslator(nodeName);
      var objPropToChange = getFromPath(obj,varPath);
      if(nodeName || objPropToChange){
          obj = assignFromPath(obj,
              varPath,
              callback(objPropToChange,$(this)));
      }
        });
       return obj;
   };
   function formElementPopulator($node,value){
       //sets the value of an element in a form. some elements require special treatment (checkboxs)
       $node.val(value);
       if($node.attr('type') == 'checkbox' && value){
     $node.attr('checked',value);
       }
   };
   return function populateForm($nodes,obj,varAtt,transformer,pather){
       if(_.isUndefined(pather)){var pathTranslator = jsPather;}
       else{var pathTranslator = pather;}

       if(!_.isUndefined(transformer)){
     obj = nodesProcessor(obj,$nodes,varAtt,transformer,pather);
       }
       $($nodes).each(function(){
      var nameAtt = $(this).attr(varAtt);
      var path = pathTranslator(nameAtt);
      var valForForm = getFromPath(obj,path);
             if(valForForm != null)//hatton added
             {
                 formElementPopulator($(this),valForForm);
             }
        });
   };
     })();