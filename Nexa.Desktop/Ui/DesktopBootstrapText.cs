using Nexa.Xsr.Renderer.Nxml;
namespace Nexa.Desktop.Ui;
internal static class DesktopBootstrapText
{
    private static readonly Lazy<Dictionary<string,string>> Terms=new(() =>
    {
        var nodes=DesktopMarkup.Load("BootstrapTerms.nxml").Root.Children;
        if(nodes.Any(node=>node.Recipe!=NxmlRuntimeRecipe.Text || string.IsNullOrWhiteSpace(node.Key)
            || node.Content is null || node.Bindings.Count!=0 || node.Children.Count!=0))
            throw new NxmlCompileException("Bootstrap terms must be keyed literal text.");
        return nodes.ToDictionary(node=>node.Key!,node=>node.Content!,StringComparer.Ordinal);
    });
    internal static string Raw(string key)=>Terms.Value[key];
    internal static string Format(string name,IReadOnlyDictionary<string,string> data,Func<string,string>? localize=null)
    {
        var node=DesktopMarkup.Dynamic("BootstrapText"+name+".d.nxml",data,localize).Root;
        if(node.Recipe!=NxmlRuntimeRecipe.Text || node.Content is null || node.Bindings.Count!=0 || node.Children.Count!=0)
            throw new NxmlCompileException("Bootstrap formats must produce literal text only.");
        return node.Content;
    }
}
