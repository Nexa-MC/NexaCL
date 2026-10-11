namespace Nexa.Services.Resources;

internal static class ResourceSearchCategories
{
    internal static void Validate(string tag)
    {
        if (tag.Length > 80 || tag.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('/' or '-' or '_'))
            || tag.Count(c => c == '/') > 1) throw new ArgumentException("资源分类无效。");
    }
    internal static string ForProvider(string tag, ResourceProvider provider)
    {
        Validate(tag);
        int slash = tag.IndexOf('/');
        if (slash < 0) return tag;
        return provider == ResourceProvider.Modrinth ? tag[(slash + 1)..] : tag[..slash];
    }
}
