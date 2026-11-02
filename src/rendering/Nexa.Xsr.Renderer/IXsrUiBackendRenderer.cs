namespace Nexa.Xsr.Renderer;

/// <summary>Input and presentation operations consumed by the native window edge; no tree access.</summary>
public interface IXsrUiBackendRenderer
{
    bool Activate(XsrUiEntityId entity);
    bool AdvanceThemeAnimations();
    XsrUiFileDrag? BeginFileDrag(XsrUiEntityId entity);
    bool CancelPointerGesture();
    void CompleteFileDrag(XsrUiEntityId entity, XsrUiFileDrag transfer);
    XsrUiContextMenuSnapshot? ContextMenuFor(XsrUiEntityId entity);
    string? CopySelectedText();
    bool EditText(XsrUiTextEdit action, bool extendSelection = false);
    XsrUiColorScheme ColorScheme { get; }
    bool EffectiveReducedMotion { get; }
    XsrUiEntityId FileDragTarget(XsrUiPoint point);
    void FinishScrollInertia(XsrUiEntityId entity);
    bool Focus(XsrUiEntityId entity, bool showIndicator = true);
    bool FocusPrevious();
    XsrUiEntityId Focused { get; }
    double GetProgressTarget(XsrUiEntityId entity);
    double GetScrollPresentationOffset(XsrUiEntityId entity);
    double GetSegmentRevealProgress(XsrUiEntityId entity);
    double GetSegmentScrollPresentationOffset(XsrUiEntityId entity);
    double GetTransitionOffset(XsrUiEntityId entity);
    double GetTransitionOffsetY(XsrUiEntityId entity);
    bool HandleKey(XsrUiKey key);
    bool HasThemeAnimations { get; }
    XsrUiEntityId HitTest(XsrUiPoint point);
    bool InsertText(string text);
    bool InvokeContextMenu(XsrUiEntityId source, XsrSemanticId command);
    bool IsScrollbarGestureActive { get; }
    string LocalizeText(string source);
    XsrUiPointerCursor PointerCursorAt(XsrUiPoint point);
    bool PointerMoved(XsrUiPoint point);
    bool PointerPressed(XsrUiPoint point);
    bool PointerReleased(XsrUiPoint point);
    bool PointerReleased(XsrUiPoint point, XsrUiClickModifiers modifiers);
    bool PointerScroll(XsrUiPoint point, double deltaY, double deltaX = 0);
    bool ReducedMotion { get; }
    bool RequestRangeValue(XsrUiEntityId entity, double value);
    void SetCapsulePresentationProgress(XsrUiEntityId entity, double progress);
    void SetPagerPresentationPosition(XsrUiEntityId entity, double position);
    void SetProgressPresentation(XsrUiEntityId entity, double value);
    void SetScrollPresentationOffset(XsrUiEntityId entity, double offset);
    void SetSegmentRevealProgress(XsrUiEntityId entity, double progress);
    void SetSegmentScrollPresentationOffset(XsrUiEntityId entity, double offset, long revision);
    bool SetTextInputValue(XsrUiEntityId entity, string value);
    bool SetTextPreedit(XsrUiEntityId entity, string? value);
    bool SetTextSelection(XsrUiEntityId entity, int start, int selectionEnd);
    void SetTransitionOffset(XsrUiEntityId entity, double offset);
    void SetTransitionOffsetY(XsrUiEntityId entity, double offset);
    IXsrUiTextLayoutProvider? TextLayoutProvider { get; set; }
}
