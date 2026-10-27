using Microsoft.AspNetCore.Components;

namespace EnshroudedSaveEditor.UI.Components;

/// <summary>
/// Browser events Blazor doesn't map by itself. <c>@onanimationend</c>: a CSS animation ended (the banner countdown); the
/// browser's own event of that name reaches the handler, with no data.
/// </summary>
[EventHandler("onanimationend", typeof(EventArgs), enableStopPropagation: true, enablePreventDefault: true)]
public static class EventHandlers
{
}
