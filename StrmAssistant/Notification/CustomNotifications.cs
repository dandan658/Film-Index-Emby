using Emby.Notifications;
using MediaBrowser.Controller;
using StrmAssistant.Properties;
using System.Collections.Generic;

namespace StrmAssistant.Notification
{
    public class CustomNotifications : INotificationTypeFactory
    {
        private readonly IServerApplicationHost _appHost;

        public CustomNotifications(IServerApplicationHost appHost) => _appHost = appHost;

        public List<NotificationTypeInfo> GetNotificationTypes(string language)
        {
            return new List<NotificationTypeInfo>
            {
                new NotificationTypeInfo { Id = "filmindex.collection", Name = "合集变更", CategoryId = "film.index", CategoryName = "Film Index Emby" },
                new NotificationTypeInfo { Id = "filmindex.metadata", Name = "元数据更新", CategoryId = "film.index", CategoryName = "Film Index Emby" },
                new NotificationTypeInfo { Id = "filmindex.images", Name = "媒体图像更新", CategoryId = "film.index", CategoryName = "Film Index Emby" },
                new NotificationTypeInfo
                {
                    Id = "favorites.update",
                    Name = Resources.Notification_CatchupUpdate_EventName,
                    CategoryId = "strm.assistant",
                    CategoryName = Resources.PluginOptions_EditorTitle_Strm_Assistant
                },
                new NotificationTypeInfo
                {
                    Id = "introskip.update",
                    Name = Resources.Notification_IntroSkipUpdate_EventName,
                    CategoryId = "strm.assistant",
                    CategoryName = Resources.PluginOptions_EditorTitle_Strm_Assistant
                }
            };
        }
    }
}
