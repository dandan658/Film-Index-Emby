using Emby.Notifications;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Notifications;
using StrmAssistant;
using StrmAssistant.Options;
using System.Reflection;

namespace FilmIndex.IntegrationTests;

public class NotificationRecorder : DispatchProxy
{
    public readonly List<NotificationRequest> Requests = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == "SendNotification" && args?[0] is NotificationRequest request) Requests.Add(request);
        return method.ReturnType == typeof(Task) ? Task.CompletedTask : null;
    }
}
internal static class NotificationChecks
{
    public static void Run(BaseItem item, ILibraryManager library, PluginOptions options, Action<PluginOptions> save, Action<bool,string> check)
    {
        var manager = DispatchProxy.Create<INotificationManager, NotificationRecorder>();
        var recorder = (NotificationRecorder)manager;
        var type = typeof(Plugin).Assembly.GetType("StrmAssistant.Common.ChangeNotificationMonitor")!;
        using var monitor = (IDisposable)Activator.CreateInstance(type, library, manager, Plugin.Instance.ApplicationHost, Plugin.Instance.Logger, null)!;
        var updated = type.GetMethod("Updated", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var changed = type.GetMethod("CollectionEntryChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var args = new ItemChangeEventArgs(library) { Item = item, CollectionFolders = Array.Empty<BaseItem>(), UpdateReason = ItemUpdateType.MetadataImport };
        options.ExperienceEnhanceOptions.NotifyMetadataChanges = false; options.ExperienceEnhanceOptions.NotifyImageChanges = false;
        options.ExperienceEnhanceOptions.NotifyCollectionChanges = false; save(options);
        updated.Invoke(monitor, new object?[] { null, args });
        check(recorder.Requests.Count == 0, "Disabled change notifications do not dispatch messages");
        options.ExperienceEnhanceOptions.NotifyMetadataChanges = true; options.ExperienceEnhanceOptions.NotifyImageChanges = true;
        options.ExperienceEnhanceOptions.NotifyCollectionChanges = true; save(options);
        updated.Invoke(monitor, new object?[] { null, args }); updated.Invoke(monitor, new object?[] { null, args });
        check(recorder.Requests.Count == 1 && recorder.Requests[0].EventId == "filmindex.metadata" && recorder.Requests[0].Item == item,
            "Metadata notifications carry the correct native item and suppress immediate duplicates");
        args.UpdateReason = ItemUpdateType.ImageUpdate; updated.Invoke(monitor, new object?[] { null, args });
        check(recorder.Requests.Last().EventId == "filmindex.images", "Image changes dispatch a separate registered event category");
        args.Parent = new BoxSet { Id = Guid.NewGuid(), Name = "Fixture collection" };
        changed.Invoke(monitor, new object?[] { null, args });
        check(recorder.Requests.Last().EventId == "filmindex.collection", "Collection member changes dispatch a collection event");
        options.ExperienceEnhanceOptions.NotifyMetadataChanges = false; options.ExperienceEnhanceOptions.NotifyImageChanges = false;
        options.ExperienceEnhanceOptions.NotifyCollectionChanges = false; save(options);
    }
}
