using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace WindowsComputerUse.Core;

public sealed partial class WindowsAutomation
{
    public ActivationResponse Activate(nint handle)
    {
        RequireWindow(handle);
        if (Native.IsIconic(handle)) Native.ShowWindow(handle, 9); // SW_RESTORE

        void RequestForeground(bool attachInputThreads)
        {
            if (!attachInputThreads)
            {
                Native.BringWindowToTop(handle);
                Native.SetActiveWindow(handle);
                Native.SetForegroundWindow(handle);
                return;
            }

            var targetThread = Native.GetWindowThreadProcessId(handle, out _);
            var currentThread = Native.GetCurrentThreadId();
            var foreground = Native.GetForegroundWindow();
            var foregroundThread = foreground == 0 ? 0 : Native.GetWindowThreadProcessId(foreground, out _);
            var attachedCurrent = false;
            var attachedForeground = false;

            try
            {
                // Retry only after ordinary foreground activation was denied. Joining
                // same-desktop input queues gives SetForegroundWindow the interactive
                // relationship it expects without crossing desktop/UIPI/UAC boundaries.
                if (targetThread != 0 && currentThread != 0 && currentThread != targetThread)
                    attachedCurrent = Native.AttachThreadInput(currentThread, targetThread, true);
                if (targetThread != 0 && foregroundThread != 0 &&
                    foregroundThread != targetThread && foregroundThread != currentThread)
                    attachedForeground = Native.AttachThreadInput(foregroundThread, targetThread, true);

                Native.BringWindowToTop(handle);
                Native.SetActiveWindow(handle);
                Native.SetForegroundWindow(handle);
            }
            finally
            {
                if (attachedForeground)
                    Native.AttachThreadInput(foregroundThread, targetThread, false);
                if (attachedCurrent)
                    Native.AttachThreadInput(currentThread, targetThread, false);
            }
        }

        if (Native.GetForegroundWindow() != handle)
            RequestForeground(attachInputThreads: false);

        if (!Native.IsIconic(handle) && Native.GetForegroundWindow() != handle)
            RequestForeground(attachInputThreads: true);

        if (!Native.IsIconic(handle) && Native.GetForegroundWindow() != handle)
        {
            // Packaged/UWP-style top-level windows (for example ApplicationFrameHost)
            // can reject the Win32 foreground path while their UIA window provider can
            // legitimately activate them. The final foreground handle remains the
            // authority; UIA SetFocus alone is never treated as success.
            try
            {
                using var automation = new UIA3Automation();
                automation.FromHandle(handle).FrameworkAutomationElement.SetFocus();
            }
            catch (Exception error) when (UiTreeReader.IsProviderFailure(error))
            {
                // Fall through to the single authoritative foreground check below.
            }
        }

        if (Native.IsIconic(handle) || Native.GetForegroundWindow() != handle)
            throw new AutomationException("activation_not_confirmed",
                "Windows did not restore and foreground this window; check session and foreground restrictions.", 409);
        return new ActivationResponse(WindowHandle.Format(handle), true);
    }

    public ElementState Inspect(nint handle, ElementLocator locator)
    {
        using var automation = new UIA3Automation();
        var element = ResolveLocator(automation, handle, locator);
        try { return ReadState(handle, element); }
        catch (Exception error) when (UiTreeReader.IsProviderFailure(error))
        {
            throw new AutomationException("element_state_unavailable", "Could not read current element state.");
        }
    }

    public ElementState Focus(nint handle, ElementLocator locator)
    {
        using var automation = new UIA3Automation();
        var element = ResolveLocator(automation, handle, locator);
        try
        {
            if (!element.Properties.IsEnabled.Value || !element.Properties.IsKeyboardFocusable.Value)
                throw new AutomationException("element_not_focusable", "Element is disabled or not keyboard-focusable.");
            // Direct UIA SetFocus; FlaUI's FocusNative attaches input threads and uses
            // Win32 SetFocus. Neither activation nor privilege elevation is implicit here.
            element.FrameworkAutomationElement.SetFocus();
            if (Native.GetForegroundWindow() != handle || !element.Properties.HasKeyboardFocus.Value)
                throw new AutomationException("focus_not_confirmed", "UI Automation did not confirm keyboard focus.", 409);
            var state = ReadState(handle, element);
            if (!state.Focused)
                throw new AutomationException("focus_not_confirmed", "UI Automation did not confirm keyboard focus.", 409);
            return state;
        }
        catch (Exception error) when (UiTreeReader.IsProviderFailure(error))
        {
            throw new AutomationException("focus_unverifiable", "UI Automation could not set or verify focus.");
        }
    }

    public void InvokeLocated(nint handle, ElementLocator locator)
    {
        using var automation = new UIA3Automation();
        var element = ResolveLocator(automation, handle, locator);
        try { InvokeElement(element); }
        catch (Exception error) when (UiTreeReader.IsProviderFailure(error))
        {
            throw new AutomationException("action_failed", "UI Automation could not invoke the resolved element.");
        }
    }

    public void SetValueLocated(nint handle, ElementLocator locator, string value)
    {
        using var automation = new UIA3Automation();
        var element = ResolveLocator(automation, handle, locator);
        try { SetValueElement(element, value); }
        catch (Exception error) when (UiTreeReader.IsProviderFailure(error))
        {
            throw new AutomationException("action_failed", "UI Automation could not set the resolved element value.");
        }
    }

    private static void InvokeElement(AutomationElement element)
    {
        if (!element.Patterns.Invoke.IsSupported)
            throw new AutomationException("unsupported_pattern", "The element does not support InvokePattern.");
        element.Patterns.Invoke.Pattern.Invoke();
    }

    private static void SetValueElement(AutomationElement element, string value)
    {
        if (!element.Patterns.Value.IsSupported)
            throw new AutomationException("unsupported_pattern", "The element does not support ValuePattern.");
        element.Patterns.Value.Pattern.SetValue(value);
    }

    private static AutomationElement ResolveLocator(UIA3Automation automation, nint handle, ElementLocator locator)
    {
        RequireWindow(handle);
        if (!InputLimits.ValidLocator(locator))
            throw new AutomationException("invalid_locator", "Use one or more nonempty exact fields and at most one ancestor.", 400);
        try
        {
            return SemanticLocator.Resolve(automation.FromHandle(handle), locator,
                element => element.FindAllChildren(), (element, criteria) =>
                    (criteria.AutomationId is null || string.Equals(element.Properties.AutomationId.ValueOrDefault,
                        criteria.AutomationId, StringComparison.Ordinal)) &&
                    (criteria.Name is null || string.Equals(element.Properties.Name.ValueOrDefault,
                        criteria.Name, StringComparison.Ordinal)) &&
                    (criteria.ControlType is null || string.Equals(element.ControlType.ToString(),
                        criteria.ControlType, StringComparison.Ordinal)));
        }
        catch (Exception error) when (UiTreeReader.IsProviderFailure(error))
        {
            throw new AutomationException("locator_provider_failed", "UI Automation could not complete the locator search.");
        }
    }

    private static ElementState ReadState(nint handle, AutomationElement element)
    {
        var rect = element.BoundingRectangle;
        var value = element.Patterns.Value.IsSupported ? element.Patterns.Value.Pattern.Value.ValueOrDefault : null;
        return new ElementState(WindowHandle.Format(handle), element.Properties.Name.ValueOrDefault, value,
            element.ControlType.ToString(), element.Properties.AutomationId.ValueOrDefault,
            element.Properties.IsEnabled.Value, element.Properties.HasKeyboardFocus.Value,
            element.Properties.IsOffscreen.Value, new RectDto(rect.Left, rect.Top, rect.Width, rect.Height),
            element.Patterns.Invoke.IsSupported, element.Patterns.Value.IsSupported);
    }
}
