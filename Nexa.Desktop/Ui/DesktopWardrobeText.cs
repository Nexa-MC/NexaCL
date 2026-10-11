using Nexa.Xsr.Renderer.Nxml;
namespace Nexa.Desktop.Ui;
internal static class DesktopWardrobeText
{
    private static readonly Lazy<Dictionary<string,string>> Terms=new(()=>ReadValues("WardrobeTerms.nxml"));
    private static readonly Lazy<Dictionary<string,string>> WardrobeDefaults=new(()=>ReadValues("WardrobeProjectionDefaults.nxml"));
    private static readonly Lazy<Dictionary<string,string>> LibraryDefaults=new(()=>ReadValues("WardrobeLibraryProjectionDefaults.nxml"));
    private static readonly Lazy<Dictionary<string,NxmlIrNode>> Metrics=new(()=>
    {
        var nodes=DesktopMarkup.Load("WardrobeMetrics.nxml").Root.Children;
        if(nodes.Any(node=>string.IsNullOrWhiteSpace(node.Key)||node.Bindings.Count!=0||node.Children.Count!=0))
            throw new NxmlCompileException("Wardrobe viewport metrics must be keyed literal leaves.");
        return nodes.ToDictionary(node=>node.Key!,StringComparer.Ordinal);
    });
    private static Dictionary<string,string> ReadValues(string name)
    {
        var nodes=DesktopMarkup.Load(name).Root.Children;
        if(nodes.Any(node=>node.Recipe!=NxmlRuntimeRecipe.Text||string.IsNullOrWhiteSpace(node.Key)||node.Content is null||node.Bindings.Count!=0||node.Children.Count!=0))
            throw new NxmlCompileException("Wardrobe values must be keyed literal text.");
        return nodes.ToDictionary(node=>node.Key!,node=>node.Content!,StringComparer.Ordinal);
    }
    internal static string Raw(string key)=>Terms.Value[key];
    internal static Dictionary<string,string> Defaults(bool library)=>new(library?LibraryDefaults.Value:WardrobeDefaults.Value,StringComparer.Ordinal);
    internal static NxmlIrNode Metric(string key)=>Metrics.Value[key];
    internal static string Format(string name,IReadOnlyDictionary<string,string> data,Func<string,string>? localize=null)
    {
        var node=DesktopMarkup.Dynamic("WardrobeText"+name+".d.nxml",data,localize).Root;
        if(node.Recipe!=NxmlRuntimeRecipe.Text||node.Content is null||node.Bindings.Count!=0||node.Children.Count!=0)
            throw new NxmlCompileException("Wardrobe formats must produce literal text only.");
        return node.Content;
    }
}
