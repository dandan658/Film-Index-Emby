using Emby.Notifications;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Concurrent;

namespace StrmAssistant.Common
{
    internal sealed class ChangeNotificationMonitor : IDisposable
    {
        private static ChangeNotificationMonitor _instance;
        private readonly ILibraryManager _library;
        private readonly INotificationManager _notifications;
        private readonly ILogger _logger;
        private readonly ICollectionManager _collections;
        private readonly ConcurrentDictionary<string, DateTimeOffset> _last = new ConcurrentDictionary<string, DateTimeOffset>();
        public ChangeNotificationMonitor(ILibraryManager library, INotificationManager notifications, IServerApplicationHost host, ILogger logger, ICollectionManager collections = null)
        {
            _library = library; _notifications = notifications; _logger = logger;
            _instance = this;
            _collections = collections;
            if (collections != null)
            { collections.ItemsAddedToCollection += CollectionMembersChanged; collections.ItemsRemovedFromCollection += CollectionMembersChanged; }
            library.ItemUpdated += Updated;
            library.ItemAdded += CollectionEntryChanged;
            library.ItemRemoved += CollectionEntryChanged;
            host.Disposed += (_, __) => Dispose();
        }
        private void CollectionMembersChanged(object sender, CollectionModifiedEventArgs args)
        {
            if (args.Collection != null && Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.NotifyCollectionChanges)
                Send("filmindex.collection", "合集内容已变更", new ItemChangeEventArgs(_library) { Item = args.Collection });
        }
        public static void Publish(BaseItem item, ItemUpdateType reason)
        {
            var current = _instance;
            current?.Updated(null, new ItemChangeEventArgs(current._library) { Item = item, UpdateReason = reason });
        }
        private void Updated(object sender, ItemChangeEventArgs args)
        {
            var options = Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions;
            if (options.NotifyImageChanges && (args.UpdateReason & ItemUpdateType.ImageUpdate) != 0)
                Send("filmindex.images", "媒体图像已更新", args);
            if (options.NotifyMetadataChanges && (args.UpdateReason & (ItemUpdateType.MetadataDownload | ItemUpdateType.MetadataEdit | ItemUpdateType.MetadataImport)) != 0)
                Send("filmindex.metadata", "媒体资料已更新", args);
            if (options.NotifyCollectionChanges && args.Item is BoxSet) Send("filmindex.collection", "合集已变更", args);
        }
        private void CollectionEntryChanged(object sender, ItemChangeEventArgs args)
        {
            if (Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.NotifyCollectionChanges && args.Parent is BoxSet)
                Send("filmindex.collection", "合集内容已变更", args);
        }
        private void Send(string eventId, string description, ItemChangeEventArgs args)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var key = eventId + ":" + args.Item.InternalId;
                if (_last.TryGetValue(key, out var previous) && now - previous < TimeSpan.FromSeconds(5)) return;
                if (_last.Count >= 1024) _last.Clear();
                _last[key] = now;
                if (_last.Count > 1024)
                    foreach (var pair in _last) if (now - pair.Value > TimeSpan.FromSeconds(5)) _last.TryRemove(pair.Key, out _);
                _notifications.SendNotification(new NotificationRequest
                {
                    Title = "Film Index Emby", EventId = eventId, Item = args.Item,
                    CollectionFolders = args.CollectionFolders,
                    Description = description + ": " + args.Item.Name
                });
            }
            catch (Exception error) { _logger.Error("FilmIndex change notification failed: " + error.Message); }
        }
        public void Dispose()
        {
            _library.ItemUpdated -= Updated; _library.ItemAdded -= CollectionEntryChanged; _library.ItemRemoved -= CollectionEntryChanged;
            if (_collections != null) { _collections.ItemsAddedToCollection -= CollectionMembersChanged; _collections.ItemsRemovedFromCollection -= CollectionMembersChanged; }
            if (ReferenceEquals(_instance, this)) _instance = null;
        }
    }
}
