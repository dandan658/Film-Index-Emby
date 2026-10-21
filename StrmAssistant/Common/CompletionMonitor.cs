using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    internal sealed class CompletionMonitor : IDisposable
    {
        private readonly ISessionManager _sessions;
        private readonly ILibraryManager _library;
        private readonly IUserManager _users;
        private readonly IUserDataManager _data;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly ConcurrentDictionary<long, byte> _busy = new ConcurrentDictionary<long, byte>();

        public CompletionMonitor(ISessionManager sessions, ILibraryManager library, IUserManager users,
            IUserDataManager data, IServerApplicationHost host, ILogger logger)
        {
            _sessions = sessions; _library = library; _users = users; _data = data; _logger = logger;
            sessions.PlaybackStopped += Stopped;
            host.Disposed += (_, __) => Dispose();
        }

        private void Stopped(object sender, PlaybackStopEventArgs args)
        {
            if (_stop.IsCancellationRequested || !args.PlayedToCompletion || args.Item is not Episode completed ||
                !Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.MarkPreviousInProgressPlayed ||
                completed.SeriesId <= 0 || args.Session == null) return;
            var userId = args.Session.UserInternalId;
            if (!_busy.TryAdd(userId, 0)) return;
            _ = Task.Run(() =>
            {
                try
                {
                    var user = _users.GetUserById(userId);
                    if (user == null) return;
                    var episodes = _library.GetItemList(new InternalItemsQuery
                    {
                        User = user, AncestorIds = new[] { completed.SeriesId },
                        IncludeItemTypes = new[] { nameof(Episode) }, Recursive = true,
                        IsPlayed = false, Limit = 10000
                    }).OfType<Episode>();
                    var changed = 0;
                    foreach (var episode in episodes)
                    {
                        _stop.Token.ThrowIfCancellationRequested();
                        if (!CompletionRules.IsEarlierEpisode(episode.ParentIndexNumber, episode.IndexNumber,
                            completed.ParentIndexNumber, completed.IndexNumber)) continue;
                        var saved = _data.GetUserData(user, episode);
                        if (saved == null || saved.Played || saved.PlaybackPositionTicks <= 0) continue;
                        saved.Played = true;
                        saved.PlaybackPositionTicks = 0;
                        saved.PlayCount = Math.Max(1, saved.PlayCount);
                        _data.SaveUserData(user, episode, saved, UserDataSaveReason.TogglePlayed, _stop.Token);
                        changed++;
                    }
                    if (changed > 0) _logger.Info("FilmIndex: marked {0} earlier in-progress episodes played for user {1}", changed, userId);
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { _logger.Error("FilmIndex completion update failed: " + error.Message); }
                finally { _busy.TryRemove(userId, out _); }
            });
        }

        public void Dispose()
        {
            _sessions.PlaybackStopped -= Stopped;
            _stop.Cancel();
        }
    }
}
