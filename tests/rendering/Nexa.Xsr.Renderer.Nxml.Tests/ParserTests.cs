namespace Nexa.Xsr.Renderer.Nxml.Tests;

internal static partial class Program
{
    private const string Header = $"<Page xmlns=\"{NxmlWellKnown.Namespace}\">";

    private static readonly string[] ExpectedChildOrder = ["Text", "Button", "Image"];

    private static void DocumentResourceBudgetsAreEnforcedBeforeRecursion()
    {
        string Nested(int count) => string.Concat(Enumerable.Repeat("<Page>", count)) + string.Concat(Enumerable.Repeat("</Page>", count));
        AssertEqual("Page", NxmlParser.Parse(Nested(128)).Root.Name);
        AssertEqual("Page", NxmlParser.Parse(Nested(128).Replace("</Page>", " <!-- comment --> </Page>", StringComparison.Ordinal)).Root.Name);
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse(Nested(20000)));
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse("<Page>" + string.Concat(Enumerable.Repeat("<Text/>", 16384)) + "</Page>"));
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse("<Page " + string.Join(' ', Enumerable.Range(0, 129).Select(i => $"A{i}=\"x\"")) + "/>"));
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse("<Page><!--" + new string('x', 1024 * 1024) + "--></Page>"));
    }

    private static void SimpleDocumentParsesStructurally()
    {
        NxmlDocument document = NxmlParser.Parse($$"""
            {{Header}}
              <StackPanel Orientation="Vertical" Spacing="8">
                <Text Content="hello" />
              </StackPanel>
            </Page>
            """);

        AssertEqual("Page", document.Root.Name);
        AssertEqual(1, document.Root.Children.Count);
        NxmlElement stack = document.Root.Children[0];
        AssertEqual("StackPanel", stack.Name);
        AssertEqual("Vertical", stack.FindProperty("Orientation")!.Value.Text);
        AssertEqual(NxmlValueKind.Literal, stack.FindProperty("Spacing")!.Value.Kind);
        AssertEqual("hello", stack.Children[0].FindProperty("Content")!.Value.Text);
    }

    private static void StateBindingsAreRecognized()
    {
        NxmlDocument document = NxmlParser.Parse($$"""
            {{Header}}
              <Text Content="{state account.name}" />
            </Page>
            """);

        NxmlValue value = document.Root.Children[0].FindProperty("Content")!.Value;
        AssertEqual(NxmlValueKind.StateBinding, value.Kind);
        AssertEqual("account.name", value.Text);
    }

    private static void NestedChildrenKeepDocumentOrder()
    {
        NxmlDocument document = NxmlParser.Parse($$"""
            {{Header}}
              <StackPanel>
                <Text Content="a" />
                <Button Label="b" />
                <Image Source="c" />
              </StackPanel>
            </Page>
            """);

        AssertSequence(
            ExpectedChildOrder,
            document.Root.Children[0].Children.Select(child => child.Name).ToArray());
    }

    private static void CommentsAndWhitespaceAreIgnored()
    {
        NxmlDocument document = NxmlParser.Parse($$"""
            {{Header}}
              <!-- a comment -->
              <Text Content="kept" />
            </Page>
            """);

        AssertEqual(1, document.Root.Children.Count);
        AssertEqual("kept", document.Root.Children[0].FindProperty("Content")!.Value.Text);
    }

    private static void DuplicatePropertiesAreRejected()
    {
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse($$"""
            {{Header}}
              <Text Content="a" Content="b" />
            </Page>
            """));
    }

    private static void MalformedBindingsAreRejected()
    {
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse($$"""
            {{Header}}
              <Text Content="{state }" />
            </Page>
            """));
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse($$"""
            {{Header}}
              <Text Content="{state a b}" />
            </Page>
            """));
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse($$"""
            {{Header}}
              <Text Content="{state account.name" />
            </Page>
            """));
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse($$"""
            {{Header}}
              <Text Content="{cmd account.name}" />
            </Page>
            """));
    }

    private static void TextContentIsRejected()
    {
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse($$"""
            {{Header}}
              <Text>hello</Text>
            </Page>
            """));
    }

    private static void DocumentsWithoutOneRootAreRejected()
    {
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse(string.Empty));
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse($$"""
            {{Header}}
            </Page>
            <Page xmlns="{{NxmlWellKnown.Namespace}}">
            </Page>
            """));
    }

    private static void ForeignNamespacesAreRejected()
    {
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse($$"""
            <Page xmlns="https://example.com/other">
            </Page>
            """));
    }

    private static void DtdAndQualifiedPropertiesAreRejected()
    {
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse("""
            <!DOCTYPE Page [<!ENTITY value "expanded">]>
            <Page xmlns="https://nexa.dev/nxml/2026">
              <Text Content="&value;" />
            </Page>
            """));
        AssertThrows<NxmlParseException>(() => NxmlParser.Parse("""
            <Page xmlns="https://nexa.dev/nxml/2026" xmlns:x="https://example.com/property">
              <Text x:Content="not-a-pxml-property" />
            </Page>
            """));
    }

    private static void DocumentLevelCommentsAreAccepted()
    {
        NxmlDocument document = NxmlParser.Parse("""
            <!-- before -->
            <Page xmlns="https://nexa.dev/nxml/2026">
              <Text Content="kept" />
            </Page>
            <!-- after -->
            """);

        AssertEqual("Page", document.Root.Name);
        AssertEqual(1, document.Root.Children.Count);
    }
}
