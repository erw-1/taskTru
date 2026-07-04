using System.Runtime.InteropServices;

namespace taskTru;

// Minimal UIA3 COM interop for VideoPlayerDetector. The managed
// System.Windows.Automation client would drag the whole WPF runtime into the
// self-contained executables (~19 MB compressed); talking to UIAutomationCore
// directly keeps the app WinForms-only.
//
// COM interop rule: members must be declared in exact vtable order, so each
// interface stubs every slot up to the last member the app calls, even unused
// ones. Do not reorder or remove the placeholder entries.
internal static class UiAutomation
{
    internal const int TreeScopeDescendants = 4;

    internal const int BoundingRectangleProperty = 30001;
    internal const int ControlTypeProperty = 30003;
    internal const int NameProperty = 30005;
    internal const int IsEnabledProperty = 30010;
    internal const int AutomationIdProperty = 30011;
    internal const int ClassNameProperty = 30012;
    internal const int IsOffscreenProperty = 30022;

    internal const int GroupControlType = 50026;
    internal const int PaneControlType = 50033;

    private static readonly Guid AutomationClassId =
        new("FF48DBA4-60EF-4201-AA87-54103EEF594E");

    [ThreadStatic]
    private static IUIAutomation? t_automation;

    // The CUIAutomation object is cheap to keep but its RCW is bound to the
    // creating apartment; detection runs on thread-pool (MTA) threads, so a
    // per-thread instance avoids cross-apartment marshaling entirely.
    internal static IUIAutomation Instance =>
        t_automation ??= (IUIAutomation)Activator.CreateInstance(
            Type.GetTypeFromCLSID(AutomationClassId)!)!;
}

[ComImport]
[Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomation
{
    void CompareElements_();                                       // 1
    void CompareRuntimeIds_();                                     // 2
    void GetRootElement_();                                        // 3
    IUIAutomationElement ElementFromHandle(nint hwnd);             // 4
    void ElementFromPoint_();                                      // 5
    void GetFocusedElement_();                                     // 6
    void GetRootElementBuildCache_();                              // 7
    void ElementFromHandleBuildCache_();                           // 8
    void ElementFromPointBuildCache_();                            // 9
    void GetFocusedElementBuildCache_();                           // 10
    void CreateTreeWalker_();                                      // 11
    void get_ControlViewWalker_();                                 // 12
    void get_ContentViewWalker_();                                 // 13
    void get_RawViewWalker_();                                     // 14
    void get_RawViewCondition_();                                  // 15
    void get_ControlViewCondition_();                              // 16
    void get_ContentViewCondition_();                              // 17
    IUIAutomationCacheRequest CreateCacheRequest();                // 18
    void CreateTrueCondition_();                                   // 19
    void CreateFalseCondition_();                                  // 20
    IUIAutomationCondition CreatePropertyCondition(                // 21
        int propertyId,
        [MarshalAs(UnmanagedType.Struct)] object value);
    void CreatePropertyConditionEx_();                             // 22
    IUIAutomationCondition CreateAndCondition(                     // 23
        IUIAutomationCondition condition1,
        IUIAutomationCondition condition2);
}

[ComImport]
[Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    void SetFocus_();                                              // 1
    void GetRuntimeId_();                                          // 2
    void FindFirst_();                                             // 3
    void FindAll_();                                               // 4
    void FindFirstBuildCache_();                                   // 5
    IUIAutomationElementArray FindAllBuildCache(                   // 6
        int scope,
        IUIAutomationCondition condition,
        IUIAutomationCacheRequest cacheRequest);
    void BuildUpdatedCache_();                                     // 7
    void GetCurrentPropertyValue_();                               // 8
    void GetCurrentPropertyValueEx_();                             // 9
    [return: MarshalAs(UnmanagedType.Struct)]
    object GetCachedPropertyValue(int propertyId);                 // 10
}

[ComImport]
[Guid("14314595-B4BC-4055-95F2-58F2E42C9855")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElementArray
{
    int Length { get; }                                            // 1
    IUIAutomationElement GetElement(int index);                    // 2
}

[ComImport]
[Guid("B32A92B5-BC25-4078-9C08-D7EE95C48E03")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationCacheRequest
{
    void AddProperty(int propertyId);                              // 1
}

[ComImport]
[Guid("352FFBA8-0973-437C-A61F-F64CAFD81DF9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationCondition
{
}
