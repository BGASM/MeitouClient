using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Ui;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    readonly UiMapView mapView = new();
    UiRect mapViewport;
    Vector2? mapDrag;
    const float MapExtent = 147456;
    static Vector2 MapPoint(Vector3 world) => new((world.X + MapExtent) / (2 * MapExtent), (MapExtent - world.Z) / (2 * MapExtent));
    static Vector3 MapWorld(Vector2 normalized) => new(normalized.X * 2 * MapExtent - MapExtent, 0, MapExtent - normalized.Y * 2 * MapExtent);
    bool OverMap(Vector2 p) => UiVisible && faithfulScreens.IsOpen("overview") && faithfulScreens.GetTab("overview", "TabsMain") == 0 && mapViewport.Contains(p.X, p.Y) && !faithfulScreens.HasModal;
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
    bool MapMoveOrder(Vector2 p, bool queued)
    {
        if (!OverMap(p)) return false;
        if (SelectedIds().Count > 0)
        {
            var at = MapWorld(mapView.At(mapViewport, p)); at.Y = ground(at.X, at.Z); Move(at, queued);
        }
        return true;
    }
    bool MapWheel(Vector2 p, float steps)
    {
        if (!OverMap(p)) return false;
        mapView.ZoomAt(mapViewport, p, MathF.Pow(1.25f, steps)); return true;
    }
    void MapAction(string widget)
    {
        if (widget == "MapCenterButton") mapView.CentreOn(MapPoint(session.Camera.Current.Target));
        else if (widget is "MapZoomInButton" or "MapZoomOutButton")
            mapView.ZoomAt(mapViewport, new(mapViewport.X + mapViewport.Width / 2, mapViewport.Y + mapViewport.Height / 2), widget == "MapZoomInButton" ? 1.5f : 1 / 1.5f);
    }
    void AddMapContent(string id, List<UiControl> controls)
    {
        if (id != "overview") return;
        int imageIndex = controls.FindIndex(c => c.Name == "MapImage");
        if (imageIndex < 0) { mapViewport = default; return; }
        var image = controls[imageIndex]; mapViewport = image.Bounds.Intersect(image.Clip ?? image.Bounds);
        var bounds = mapView.ImageBounds(mapViewport);
        controls[imageIndex] = image with { Bounds = bounds, Clip = mapViewport, Pickable = true };
        void Marker(string name, Vector3 at, string group, float size, Vector4 colour)
        {
            var normalized = MapPoint(at);
            var marker = new UiRect(bounds.X + normalized.X * bounds.Width - size / 2, bounds.Y + normalized.Y * bounds.Height - size / 2, size, size);
            if (marker.Intersect(mapViewport).Width <= 0 || marker.Intersect(mapViewport).Height <= 0) return;
            var props = new Dictionary<string, string> { ["ImageResource"] = "Kenshi_MapMarkers", ["ImageGroup"] = group, ["ImageName"] = group,
                ["Colour"] = FormattableString.Invariant($"{colour.X} {colour.Y} {colour.Z} {colour.W}") };
            controls.Insert(++imageIndex, new(name, "ImageBox", marker, "", "", "", true, props, default, Pickable: false, Clip: mapViewport));
        }
        foreach (var character in session.CurrentSnapshot.Characters.Where(c => c.IsPlayer))
            Marker("map-character-" + character.Id, character.Position, "Character", character.Selected ? 20 : 16, character.Selected ? new(0.4f, 1, 0.4f, 1) : Vector4.One);
        Marker("map-camera", session.Camera.Current.Target, "Camera", 22, Vector4.One);
    }
}
