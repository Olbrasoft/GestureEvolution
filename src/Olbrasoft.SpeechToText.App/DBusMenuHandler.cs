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

        var layout = BuildMenuLayout(parentId, recursionDepth, propertyNames);
        return ValueTask.FromResult((_revision, layout));
    }

    private (int, Dictionary<string, VariantValue>, VariantValue[]) BuildMenuLayout(int parentId, int recursionDepth, string[] propertyNames)
    {
        if (parentId == RootId)
        {
            // Root menu with children
            var rootProps = new Dictionary<string, VariantValue>
            {
                ["children-display"] = new VariantValue("submenu")
            };

            VariantValue[] children;
            if (recursionDepth == 0)
            {
                // No children requested
                children = Array.Empty<VariantValue>();
            }
            else
            {
                // Build children (About, separator, Quit)
                var aboutItem = BuildMenuItem(AboutId, "About", recursionDepth - 1);
                var separatorItem = BuildSeparator(SeparatorId);
                var quitItem = BuildMenuItem(QuitId, "Quit", recursionDepth - 1);

                children = new VariantValue[]
                {
                    VariantValue.Struct(aboutItem.Item1, aboutItem.Item2, aboutItem.Item3),
                    VariantValue.Struct(separatorItem.Item1, separatorItem.Item2, separatorItem.Item3),
                    VariantValue.Struct(quitItem.Item1, quitItem.Item2, quitItem.Item3)
                };
            }

            return (RootId, rootProps, children);
        }

        // For non-root items, return the item without children
        return parentId switch
        {
            AboutId => BuildMenuItem(AboutId, "About", 0),
            SeparatorId => BuildSeparator(SeparatorId),
            QuitId => BuildMenuItem(QuitId, "Quit", 0),
            _ => (parentId, new Dictionary<string, VariantValue>(), Array.Empty<VariantValue>())
        };
    }

    private (int, Dictionary<string, VariantValue>, VariantValue[]) BuildMenuItem(int id, string label, int remainingDepth)
    {
        var props = new Dictionary<string, VariantValue>
        {
            ["label"] = new VariantValue(label),
            ["enabled"] = new VariantValue(true),
            ["visible"] = new VariantValue(true)
        };

        return (id, props, Array.Empty<VariantValue>());
    }

    private (int, Dictionary<string, VariantValue>, VariantValue[]) BuildSeparator(int id)
    {
        var props = new Dictionary<string, VariantValue>
        {
            ["type"] = new VariantValue("separator"),
            ["visible"] = new VariantValue(true)
        };

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
                ["children-display"] = new VariantValue("submenu")
            }),
            AboutId => (id, new Dictionary<string, VariantValue>
            {
                ["label"] = new VariantValue("About"),
                ["enabled"] = new VariantValue(true),
                ["visible"] = new VariantValue(true)
            }),
            SeparatorId => (id, new Dictionary<string, VariantValue>
            {
                ["type"] = new VariantValue("separator"),
                ["visible"] = new VariantValue(true)
            }),
            QuitId => (id, new Dictionary<string, VariantValue>
            {
                ["label"] = new VariantValue("Quit"),
                ["enabled"] = new VariantValue(true),
                ["visible"] = new VariantValue(true)
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
        return ValueTask.FromResult(new VariantValue(""));
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
