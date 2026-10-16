using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Ui;
using Meitou.Simulation;
using Meitou.Ui;

namespace Meitou.Game;

/// <summary>How a town's faction stands to the player, for its map colour.</summary>
public enum MapStanding { Neutral, Ally, Enemy, Player }

/// <summary>The world map (Overview's MAP tab), after the original's MapScreen (docs/game/ui-screens.md "Map").</summary>
sealed partial class PlayerInterface
{
    readonly UiMapView mapView = new() { FitWhole = true };
    UiRect mapViewport;
    Vector2? mapDrag;
    bool mapRightPressed, mapShown;
    Vector3 movementMarker;
    long movementMarkerUntil;
    readonly Dictionary<string, Vector2> markerSizes = new(StringComparer.Ordinal);

    /// <summary>The placed towns (the population's sites, in the order of <see cref="WorldSnapshot.Towns"/>); the host sets it.</summary>
    public Func<IReadOnlyList<TownSite>>? MapTowns { get; set; }
    /// <summary>How a faction (by string id) stands to the player; the host sets it.</summary>
    public Func<string?, MapStanding>? MapStandingOf { get; set; }

    const float MapExtent = 147456, MapCanvas = 4096, MovementMarkerSeconds = 3, NestShowRange = 3258;
    // MarkerAlly, MarkerNeutral, MarkerEnemy, MarkerPlayer and MarkerMovement of data/gui/colours/kenshi_colours.xml; PlayerSelected is the game's built-in one (not in the file).
    static readonly Vector4 AllyColour = Rgb(0x5cb473), NeutralColour = Rgb(0xdfdfdf), EnemyColour = Rgb(0xe65139), PlayerColour = Rgb(0x8dcdff),
        MovementColour = Rgb(0x79b44b), SelectedColour = new(0.652f, 0.903f, 1, 1);
    static Vector4 Rgb(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);

    // The map image spans the world's 294912 units with world (0, 0) at its centre, +X right and +Z down (Verified: GUI_Map.dds against the placed towns).
    static Vector2 MapPoint(Vector3 world) => new((world.X + MapExtent) / (2 * MapExtent), (world.Z + MapExtent) / (2 * MapExtent));
    static Vector3 MapWorld(Vector2 normalized) => new(normalized.X * 2 * MapExtent - MapExtent, 0, normalized.Y * 2 * MapExtent - MapExtent);

    float UiScale => Height / 1080f;
    bool MapTabOpen => UiVisible && faithfulScreens.IsOpen("overview") && faithfulScreens.GetTab("overview", "TabsMain") == 0;
    bool OverMap(Vector2 p) => MapTabOpen && mapViewport.Contains(p.X, p.Y) && !faithfulScreens.HasModal;
    /// <summary>The original's map scale: on-screen pixels per pixel of the 4096 canvas. The least fits the whole map, the most is 1.5 x the UI scale.</summary>
    float MapScale => mapView.Size(mapViewport) / MapCanvas;
    float MapMinScale => Math.Min(mapViewport.Width, mapViewport.Height) / MapCanvas;
    /// <summary>0 below 0.4 x the UI scale (no characters or labels, small towns), 1 to 0.75 x, else 2.</summary>
    int MapZoomLevel => MapScale < 0.4f * UiScale ? 0 : MapScale <= 0.75f * UiScale ? 1 : 2;

    /// <summary>Left or middle button: pans.</summary>
    bool MapPointerDown(Vector2 p)
    {
        if (!OverMap(p)) return false;
        mapDrag = p; return true;
    }
    bool MapPointerMove(Vector2 p)
    {
        if (mapDrag is not { } previous) return false;
        mapView.Pan(mapViewport, p - previous); mapDrag = p; return true;
    }
    /// <summary>The right button does nothing until it comes up (<see cref="MapRightUp"/>).</summary>
    bool MapRightDown(Vector2 p)
    {
        if (!OverMap(p)) return false;
        mapRightPressed = true; return true;
    }
    /// <summary>
    /// A move order for the selection to the world point under the cursor (clamped to the map), its height the ground's but at least 100. Shift does not queue, as in the
    /// original; the movement marker shows for 3 s.
    /// </summary>
    bool MapRightUp(Vector2 p)
    {
        if (!mapRightPressed) return false;
        mapRightPressed = false;
        if (!OverMap(p)) return true;
        if (SelectedIds().Count > 0)
        {
            var at = MapWorld(Vector2.Clamp(mapView.At(mapViewport, p), Vector2.Zero, Vector2.One));
            at.Y = MathF.Max(ground(at.X, at.Z), 100);
            Move(at, queued: false);
            movementMarker = at;
            movementMarkerUntil = Stopwatch.GetTimestamp() + (long)(MovementMarkerSeconds * Stopwatch.Frequency);
        }
        return true;
    }
    /// <summary>The wheel adds 0.075 to the scale per notch, about the cursor.</summary>
    bool MapWheel(Vector2 p, float steps)
    {
        if (!OverMap(p)) return false;
        MapScaleAt(p, MapScale + 0.075f * steps); return true;
    }
    void MapScaleAt(Vector2 p, float scale)
    {
        if (MapScale <= 0) return;
        mapView.ZoomAt(mapViewport, p, Math.Max(scale, MapMinScale) / MapScale);
    }
    void MapAction(string widget)
    {
        if (widget == "MapCenterButton") mapView.CentreOn(MapPoint(session.Camera.Current.Target));
        else if (widget is "MapZoomInButton" or "MapZoomOutButton")
            MapScaleAt(new(mapViewport.X + mapViewport.Width / 2, mapViewport.Y + mapViewport.Height / 2), MapScale + (widget == "MapZoomInButton" ? MapMinScale : -MapMinScale));
    }

    /// <summary>Once a frame: opening the map centres it on the camera and hides the movement marker; closing the overview on it marks the new discoveries seen.</summary>
    void UpdateMap()
    {
        bool shown = MapTabOpen;
        if (shown && !mapShown)
        {
            mapView.CentreOn(MapPoint(session.Camera.Current.Target));
            movementMarkerUntil = 0;
        }
        if (!shown && mapShown && !faithfulScreens.IsOpen("overview") && session.CurrentSnapshot.Towns.Any(t => (t & TownKnowledge.Recent) != 0))
            Say(new SeenDiscoveriesCommand { Tick = session.World.Tick });
        if (!shown) { mapDrag = null; mapRightPressed = false; }
        mapShown = shown;
    }

    void AddMapContent(string id, List<UiControl> controls)
    {
        if (id != "overview") return;
        int imageIndex = controls.FindIndex(c => c.Name == "MapImage");
        if (imageIndex < 0) { mapViewport = default; return; }
        var image = controls[imageIndex]; mapViewport = image.Bounds.Intersect(image.Clip ?? image.Bounds);
        if (mapViewport.Width <= 0 || mapViewport.Height <= 0) return;
        float ui = UiScale;
        mapView.MaxZoom = 1.5f * ui / MapMinScale;
        var bounds = mapView.ImageBounds(mapViewport);
        controls[imageIndex] = image with { Bounds = bounds, Clip = mapViewport, Pickable = true };
        int level = MapZoomLevel;
        Vector2 Screen(Vector3 world) { var n = MapPoint(world); return new(bounds.X + n.X * bounds.Width, bounds.Y + n.Y * bounds.Height); }
        Vector2 GroupSize(string group)
        {
            if (markerSizes.TryGetValue(group, out var known)) return known;
            var props = new Dictionary<string, string> { ["ImageResource"] = "Kenshi_MapMarkers", ["ImageGroup"] = group, ["ImageName"] = group };
            return markerSizes[group] = MyGuiImages.Resolve(uiResources, props) is { } i ? new(i.Source.Width, i.Source.Height) : new(32);
        }
        void Marker(string name, string group, UiRect box, Vector4 colour, float angle = 0)
        {
            if (box.Intersect(mapViewport).Width <= 0 || box.Intersect(mapViewport).Height <= 0) return;
            var props = new Dictionary<string, string> { ["ImageResource"] = "Kenshi_MapMarkers", ["ImageGroup"] = group, ["ImageName"] = group,
                ["Colour"] = FormattableString.Invariant($"{colour.X} {colour.Y} {colour.Z} {colour.W}") };
            if (angle != 0) props["ImageAngle"] = angle.ToString(CultureInfo.InvariantCulture);
            controls.Insert(++imageIndex, new(name, "ImageBox", box, "", "", "", true, props, default, Pickable: false, Clip: mapViewport));
        }
        void Label(string name, string text, Vector2 topCentre, Vector4 colour)
        {
            float width = 400 * ui, height = 24 * ui;
            var props = new Dictionary<string, string> { ["FontName"] = "Kenshi_StandardFont_Medium", ["TextAlign"] = "HCenter Top" };
            // MyGUI's text shadow: the text again in black, a pixel down and right, under it.
            controls.Insert(++imageIndex, new(name + "-shadow", "TextBox", new(topCentre.X - width / 2 + 1, topCentre.Y + 1, width, height), text, "", "", true, props, default,
                new Vector4(0, 0, 0, colour.W), Pickable: false, TextScale: ui, Clip: mapViewport));
            controls.Insert(++imageIndex, new(name, "TextBox", new(topCentre.X - width / 2, topCentre.Y, width, height), text, "", "", true, props, default,
                colour, Pickable: false, TextScale: ui, Clip: mapViewport));
        }

        // Towns: discovered ones only; which zoom levels show each kind, its size and its label (docs/game/ui-screens.md "Map").
        var snapshot = session.CurrentSnapshot;
        var players = snapshot.Characters.Where(c => c.IsPlayer).ToList();
        if (MapTowns?.Invoke() is { } towns)
            for (int i = 0; i < towns.Count && i < snapshot.Towns.Count; i++)
            {
                var known = snapshot.Towns[i];
                var site = towns[i];
                var type = site.Town.Type;
                if ((known & TownKnowledge.Discovered) == 0 || type is TownType.NestMarker or TownType.Null) continue;
                if (type == TownType.Nest && !players.Any(c => Vector2.DistanceSquared(new(c.Position.X, c.Position.Z), new(site.Position.X, site.Position.Z)) <= NestShowRange * NestShowRange)) continue;
                bool recent = (known & TownKnowledge.Recent) != 0;
                int least = recent || type == TownType.Town ? 0 : type is TownType.Nest or TownType.PointOfInterest ? 2 : 1;
                if (level < least) continue;
                string group = type switch
                {
                    TownType.Nest => "Nest", TownType.Outpost or TownType.Military or TownType.Prison => "Outpost", TownType.Town => "Town", TownType.Village => "Village",
                    TownType.Ruins => "Ruin", TownType.SlaveCamp => "Workcamp", _ => "SmallPlace",
                };
                float factor = level == 0 ? 0.5f : type == TownType.Town ? (level == 1 ? 0.75f : 1) : 0.85f;
                var size = GroupSize(group) * ui * factor;
                var centre = Screen(site.Position);
                var box = new UiRect(centre.X - size.X / 2, centre.Y - size.Y / 2, size.X, size.Y);
                bool explored = (known & TownKnowledge.Explored) != 0;
                var colour = !explored ? NeutralColour : (MapStandingOf?.Invoke(site.Town.Faction) ?? MapStanding.Neutral) switch
                {
                    MapStanding.Player => PlayerColour, MapStanding.Ally => AllyColour, MapStanding.Enemy => EnemyColour, _ => NeutralColour,
                };
                Marker("map-town-" + i, group, box, colour);
                if (recent)
                {
                    var mark = GroupSize("NewDiscovered") * ui * 0.5f;
                    Marker("map-town-new-" + i, "NewDiscovered", new(box.X + box.Width - mark.X / 2, box.Y - mark.Y / 2, mark.X, mark.Y), Vector4.One);
                }
                if (level > 0 && type != TownType.Nest)
                    Label("map-town-label-" + i, explored || site.Town.UnexploredName.Length == 0 ? site.Town.Name : site.Town.UnexploredName,
                        new(centre.X, box.Y + box.Height + MathF.Round(3.2f * ui)), colour);
            }

        // The player's characters, hidden at the least zoom level.
        if (level > 0)
        {
            var size = GroupSize("Character") * ui;
            foreach (var character in players)
            {
                var at = Screen(where(character));
                Marker("map-character-" + character.Id, "Character", new(at.X - size.X / 2, at.Y - size.Y / 2, size.X, size.Y), character.Selected ? SelectedColour : PlayerColour);
            }
        }

        // The movement marker stands on the ordered point (its bottom centre) for 3 s.
        if (Stopwatch.GetTimestamp() < movementMarkerUntil)
        {
            var size = GroupSize("Movement") * ui;
            var at = Screen(movementMarker);
            Marker("map-movement", "Movement", new(at.X - size.X / 2, at.Y - size.Y, size.X, size.Y), MovementColour);
        }

        // The camera, turned to its view: the angle between the level view direction and -Z, negative towards -X.
        {
            var camera = session.Camera.Current;
            var look = new Vector2(camera.Forward.X, camera.Forward.Z);
            float angle = look.LengthSquared() > 1e-8f ? MathF.Acos(Math.Clamp(-Vector2.Normalize(look).Y, -1, 1)) * (look.X < 0 ? -1 : 1) : 0;
            var size = GroupSize("Camera") * ui;
            var at = Screen(camera.Target);
            Marker("map-camera", "Camera", new(at.X - size.X / 2, at.Y - size.Y / 2, size.X, size.Y), PlayerColour, angle);
        }
    }
}
