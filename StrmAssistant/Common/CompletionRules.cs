namespace StrmAssistant.Common
{
    public static class CompletionRules
    {
        public static bool IsEarlierEpisode(int? season, int? episode, int? completedSeason, int? completedEpisode)
        {
            if (!(season > 0) || !(episode > 0) || !(completedSeason > 0) || !(completedEpisode > 0)) return false;
            return season < completedSeason || season == completedSeason && episode < completedEpisode;
        }
    }
}
