using StrmAssistant.Options;
using System;

namespace StrmAssistant.Web.Helper
{
    public static class PresentationScript
    {
        private static string Value(EpisodeViewStyle style) => style switch
        { EpisodeViewStyle.Primary => "primary", EpisodeViewStyle.Thumb => "thumb", EpisodeViewStyle.List => "list", EpisodeViewStyle.Table => "datagrid", _ => null };
        public static string Injection(EpisodeViewStyle season, EpisodeViewStyle episode)
        {
            var seasonValue = Value(season); var episodeValue = Value(episode);
            if (seasonValue == null && episodeValue == null) return string.Empty;
            return @"(function(){
var original=ListController.prototype.getViewSettings;
ListController.prototype.getViewSettings=function(){
var result=original.apply(this,arguments),preset=this.getDisplayPreset(),style=null;
if(preset==='Season')style=" + (seasonValue == null ? "null" : "'" + seasonValue + "'") + @";
if(preset==='Episode')style=" + (episodeValue == null ? "null" : "'" + episodeValue + "'") + @";
if(!style)return result;
var updated=Object.assign({},result,{imageType:style});
if(style==='datagrid'&&result.tableDefaultFields)updated.fields=result.tableDefaultFields.slice();
else if(result.defaultFields)updated.fields=result.defaultFields.slice();
return updated;
};})();";
        }
        public static string Build(string native, EpisodeViewStyle season, EpisodeViewStyle episode)
        {
            const string export = "_exports.default=ListController";
            var injection = Injection(season, episode);
            if (injection.Length == 0) return native;
            var index = native.IndexOf(export, StringComparison.Ordinal);
            if (index < 0 || native.IndexOf(export, index + export.Length, StringComparison.Ordinal) >= 0)
            { Plugin.Instance.Logger.Warn("FilmIndex: unknown web list-controller format; native presentation preserved"); return native; }
            return native.Insert(index, injection);
        }
    }
}
