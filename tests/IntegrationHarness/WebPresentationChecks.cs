using StrmAssistant.Options;
using StrmAssistant.Web.Helper;

namespace FilmIndex.IntegrationTests;

internal static class WebPresentationChecks
{
    public static async Task Run(string root, PluginOptions options, Action<PluginOptions> save, Action<bool,string> check)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var nativePath = Path.Combine(StrmAssistant.Plugin.Instance.ApplicationPaths.ProgramSystemPath, "dashboard-ui", "modules", "tabbedview", "listcontroller.js");
        var original = await File.ReadAllTextAsync(nativePath);
        options.ExperienceEnhanceOptions.SeasonViewStyle = EpisodeViewStyle.Native;
        options.ExperienceEnhanceOptions.EpisodeViewStyle = EpisodeViewStyle.Native; save(options);
        var native = await http.GetStringAsync("http://127.0.0.1:18766/emby/web/modules/tabbedview/listcontroller.js");
        check(native == original, "Web presentation endpoint serves the exact installed native module when both options are disabled");
        options.ExperienceEnhanceOptions.SeasonViewStyle = EpisodeViewStyle.Primary;
        options.ExperienceEnhanceOptions.EpisodeViewStyle = EpisodeViewStyle.Table; save(options);
        var patched = await http.GetStringAsync("http://127.0.0.1:18766/emby/web/modules/tabbedview/listcontroller.js");
        var injection = PresentationScript.Injection(EpisodeViewStyle.Primary, EpisodeViewStyle.Table);
        check(patched == PresentationScript.Build(original, EpisodeViewStyle.Primary, EpisodeViewStyle.Table) && patched.Contains(injection),
            "Web presentation endpoint wraps the installed 4.10 module with independent season and episode styles");
        await File.WriteAllTextAsync(Path.Combine(root, "listcontroller-patched.js"), patched);
        await File.WriteAllTextAsync(Path.Combine(root, "presentation-injection.js"), injection);
        options.ExperienceEnhanceOptions.SeasonViewStyle = EpisodeViewStyle.Native;
        options.ExperienceEnhanceOptions.EpisodeViewStyle = EpisodeViewStyle.Native; save(options);
        check(await http.GetStringAsync("http://127.0.0.1:18766/emby/web/modules/tabbedview/listcontroller.js") == original,
            "Disabling forced presentation immediately restores the native script without editing Emby files");
    }
}
