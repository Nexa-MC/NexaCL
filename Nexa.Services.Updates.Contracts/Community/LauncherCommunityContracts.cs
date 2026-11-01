namespace Nexa.Services.Community;

public sealed record LauncherAnnouncement(string Id, long Revision, string Title, string Body);
public sealed record LauncherCommunitySnapshot(bool SurveyDue, IReadOnlyList<LauncherAnnouncement> Announcements);
