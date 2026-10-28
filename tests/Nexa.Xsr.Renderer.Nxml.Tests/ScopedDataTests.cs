namespace Nexa.Xsr.Renderer.Nxml.Tests;
internal static partial class Program
{
    private static void ExtensionDataScopesReferencesWithoutRelaxingBudgets()
    {
        var values = Enumerable.Range(0, 500).ToDictionary(i => "unused" + i, _ => "unused");
        values["name"] = "User"; values["hidden"] = "false";
        var components = NxmlComponentLibrary.Default.WithComponents(new Dictionary<string, string>
        { ["Row"] = """<nxml-component><Text Content="Hello {data name}" IsVisible="{data !hidden}" /></nxml-component>""" });
        var template = new NxmlDynamicTemplate("""<nxml-dynamic><Row /></nxml-dynamic>""");
        _ = template.CompileScoped(values, components);
        AssertThrows<NxmlCompileException>(() => template.Compile(values, components));
        values.Remove("name");
        AssertThrows<NxmlCompileException>(() => template.CompileScoped(values, components));
        values["name"] = new string('x', 1024 * 1024 + 1);
        AssertThrows<NxmlCompileException>(() => template.CompileScoped(values, components));
        var many = new NxmlDynamicTemplate("<nxml-dynamic><StackPanel>" + string.Concat(Enumerable.Range(0, 257)
            .Select(i => "<Text Content=\"{data unused" + i + "}\" />")) + "</StackPanel></nxml-dynamic>");
        AssertThrows<NxmlCompileException>(() => many.CompileScoped(values, components));
    }
}
