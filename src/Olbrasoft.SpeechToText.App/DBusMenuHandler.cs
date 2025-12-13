using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;
using Tmds.DBus.SourceGenerator;

namespace Olbrasoft.SpeechToText.App;

/// <summary>
/// D-Bus handler for com.canonical.dbusmenu interface.
/// Provides context menu for the tray icon with Quit and About items.
/// </summary>
internal class DBusMenuHandler : ComCanonicalDbusmenuHandler
{
    private readonly Connection _connection;
    private readonly ILogger _logger;
    private uint _revision = 1;

    // Menu item IDs
    private const int RootId = 0;
    private const int AboutId = 1;
    private const int SeparatorId = 2;
    private const int QuitId = 3;

    /// <summary>
    /// Event fired when user selects Quit from the menu.
    /// </summary>
    public event Action? OnQuitRequested;

    /// <summary>
    /// Event fired when user selects About from the menu.
    /// </summary>
    public event Action? OnAboutRequested;

    public DBusMenuHandler(Connection connection, ILogger logger) : base(emitOnCapturedContext: false)
    {
        _connection = connection;
        _logger = logger;

        // Set D-Bus properties
        Version = 3; // dbusmenu protocol version
        TextDirection = "ltr";
        Status = "normal";
        IconThemePath = Array.Empty<string>();
    }

    public override Connection Connection => _connection;

    /// <summary>
    /// Returns the menu layout starting from the specified parent ID.
    /// </summary>
    protected override ValueTask<(uint Revision, (int, Dictionary<string, VariantValue>, VariantValue[]) Layout)> OnGetLayoutAsync(
        Message request, int parentId, int recursionDepth, string[] propertyNames)
    {
        _logger.LogDebug("GetLayout: parentId={ParentId}, depth={Depth}", parentId, recursionDepth);

        var layout = BuildMenuLayout(parentId, recursionDepth);
        return ValueTask.FromResult((_revision, layout));
    }

    private (int, Dictionary<string, VariantValue>, VariantValue[]) BuildMenuLayout(int parentId, int recursionDepth)
    {
        if (parentId == RootId)
        {
            // Root menu with children
            var rootProps = new Dictionary<string, VariantValue>
            {
                ["children-display"] = VariantValue.String("submenu")
            };

            // For the root menu, return children as variants containing menu item structs
            // Each child is serialized as (ia{sv}av) wrapped in a variant
            VariantValue[] children;
            if (recursionDepth == 0)
            {
                children = Array.Empty<VariantValue>();
            }
            else
            {
                // Build child menu items
                // Since we can't easily create nested struct variants, we'll use a workaround:
                // Return child IDs only and let the shell query individual items
                children = new VariantValue[]
                {
                    CreateChildVariant(AboutId, "About", false),
                    CreateChildVariant(SeparatorId, "", true),
                    CreateChildVariant(QuitId, "Quit", false)
                };
            }

            return (RootId, rootProps, children);
        }

        // For non-root items, return the specific item
        return GetMenuItemLayout(parentId);
    }

    private VariantValue CreateChildVariant(int id, string label, bool isSeparator)
    {
        // Create a struct variant for menu item: (ia{sv}av)
        // We need to create this as a D-Bus struct variant

        // Build properties dictionary
        var props = new Dict<string, VariantValue>();
        if (isSeparator)
        {
            props.Add("type", VariantValue.String("separator"));
            props.Add("visible", VariantValue.Bool(true));
        }
        else
        {
            props.Add("label", VariantValue.String(label));
            props.Add("enabled", VariantValue.Bool(true));
            props.Add("visible", VariantValue.Bool(true));
        }

        // Empty children array for leaf items
        var children = new Array<VariantValue>();

        // Create the struct (ia{sv}av)
        return Struct.Create(id, props, children);
    }

    private (int, Dictionary<string, VariantValue>, VariantValue[]) GetMenuItemLayout(int id)
    {
        var props = new Dictionary<string, VariantValue>();

        switch (id)
        {
            case AboutId:
                props["label"] = VariantValue.String("About");
                props["enabled"] = VariantValue.Bool(true);
                props["visible"] = VariantValue.Bool(true);
                break;
            case SeparatorId:
                props["type"] = VariantValue.String("separator");
                props["visible"] = VariantValue.Bool(true);
                break;
            case QuitId:
                props["label"] = VariantValue.String("Quit");
                props["enabled"] = VariantValue.Bool(true);
                props["visible"] = VariantValue.Bool(true);
                break;
        }

        return (id, props, Array.Empty<VariantValue>());
    }

    /// <summary>
    /// Returns properties for multiple menu items.
    /// </summary>
    protected override ValueTask<(int, Dictionary<string, VariantValue>)[]> OnGetGroupPropertiesAsync(
        Message request, int[] ids, string[] propertyNames)
    {
        _logger.LogDebug("GetGroupProperties: ids=[{Ids}]", string.Join(",", ids));

        var results = ids.Select(id => GetItemProperties(id)).ToArray();
        return ValueTask.FromResult(results);
    }

    private (int, Dictionary<string, VariantValue>) GetItemProperties(int id)
    {
        return id switch
        {
            RootId => (id, new Dictionary<string, VariantValue>
            {
                ["children-display"] = VariantValue.String("submenu")
            }),
            AboutId => (id, new Dictionary<string, VariantValue>
            {
                ["label"] = VariantValue.String("About"),
                ["enabled"] = VariantValue.Bool(true),
                ["visible"] = VariantValue.Bool(true)
            }),
            SeparatorId => (id, new Dictionary<string, VariantValue>
            {
                ["type"] = VariantValue.String("separator"),
                ["visible"] = VariantValue.Bool(true)
            }),
            QuitId => (id, new Dictionary<string, VariantValue>
            {
                ["label"] = VariantValue.String("Quit"),
                ["enabled"] = VariantValue.Bool(true),
                ["visible"] = VariantValue.Bool(true)
            }),
            _ => (id, new Dictionary<string, VariantValue>())
        };
    }

    /// <summary>
    /// Returns a single property of a menu item.
    /// </summary>
    protected override ValueTask<VariantValue> OnGetPropertyAsync(Message request, int id, string name)
    {
        _logger.LogDebug("GetProperty: id={Id}, name={Name}", id, name);

        var props = GetItemProperties(id).Item2;
        if (props.TryGetValue(name, out var value))
        {
            return ValueTask.FromResult(value);
        }

        // Return empty string for unknown properties
        return ValueTask.FromResult(VariantValue.String(""));
    }

    /// <summary>
    /// Handles menu events (clicks).
    /// </summary>
    protected override ValueTask OnEventAsync(Message request, int id, string eventId, VariantValue data, uint timestamp)
    {
        _logger.LogDebug("Event: id={Id}, eventId={EventId}", id, eventId);

        if (eventId == "clicked")
        {
            switch (id)
            {
                case QuitId:
                    _logger.LogInformation("Quit menu item clicked");
                    OnQuitRequested?.Invoke();
                    break;
                case AboutId:
                    _logger.LogInformation("About menu item clicked");
                    OnAboutRequested?.Invoke();
                    break;
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Handles batch menu events.
    /// </summary>
    protected override ValueTask<int[]> OnEventGroupAsync(Message request, (int, string, VariantValue, uint)[] events)
    {
        _logger.LogDebug("EventGroup: {Count} events", events.Length);

        foreach (var (id, eventId, data, timestamp) in events)
        {
            _ = OnEventAsync(request, id, eventId, data, timestamp);
        }

        return ValueTask.FromResult(Array.Empty<int>());
    }

    /// <summary>
    /// Called before showing a menu item. Returns whether the menu needs update.
    /// </summary>
    protected override ValueTask<bool> OnAboutToShowAsync(Message request, int id)
    {
        _logger.LogDebug("AboutToShow: id={Id}", id);
        return ValueTask.FromResult(false); // No update needed
    }

    /// <summary>
    /// Called before showing multiple menu items.
    /// </summary>
    protected override ValueTask<(int[] UpdatesNeeded, int[] IdErrors)> OnAboutToShowGroupAsync(Message request, int[] ids)
    {
        _logger.LogDebug("AboutToShowGroup: ids=[{Ids}]", string.Join(",", ids));
        return ValueTask.FromResult((Array.Empty<int>(), Array.Empty<int>()));
    }
}
