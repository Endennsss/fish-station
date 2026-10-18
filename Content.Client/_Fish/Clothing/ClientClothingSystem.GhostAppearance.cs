using Content.Shared._Fish.Ghost;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Client.Clothing;

public sealed partial class ClientClothingSystem
{
    /// <summary>
    /// Resolves saved clothing visuals after the original item and body have left PVS or been deleted.
    /// </summary>
    public List<PrototypeLayerData> GetGhostAppearanceLayers(GhostClothingAppearance appearance)
    {
        RSI? rsi = null;
        if (appearance.RsiPath is { } path &&
            _cache.TryGetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / path, out var resource))
            rsi = resource.RSI;
        else if (appearance.Prototype is { } prototypeId &&
                 _prototype.TryIndex(prototypeId, out var prototype) &&
                 prototype.TryGetComponent<SpriteComponent>(out var sprite, Factory))
            rsi = sprite.BaseRSI;

        if (appearance.Layers is { } explicitLayers)
        {
            var result = new List<PrototypeLayerData>(explicitLayers.Count);
            foreach (var layer in explicitLayers)
            {
                result.Add(new PrototypeLayerData
                {
                    RsiPath = layer.RsiPath ?? rsi?.Path.ToString(),
                    TexturePath = layer.TexturePath,
                    State = layer.State,
                    Color = layer.Color,
                    Scale = layer.Scale,
                    Offset = (layer.Offset ?? default) + appearance.Offset,
                    Rotation = layer.Rotation,
                    Visible = layer.Visible,
                    Cycle = layer.Cycle,
                    Loop = layer.Loop,
                });
            }
            return result;
        }

        if (rsi == null || !TemporarySlotMap.TryGetValue(appearance.Slot, out var slot))
            return [];

        var state = appearance.EquippedState ?? (string.IsNullOrEmpty(appearance.EquippedPrefix)
            ? $"equipped-{slot}"
            : $"{appearance.EquippedPrefix}-equipped-{slot}");
        if (appearance.BodyType != null && rsi.TryGetState($"{state}-{appearance.BodyType}", out _))
            state = $"{state}-{appearance.BodyType}";
        if (appearance.Species != null && rsi.TryGetState($"{state}-{appearance.Species}", out _))
            state = $"{state}-{appearance.Species}";
        if (!rsi.TryGetState(state, out _))
            return [];

        // Явная инициализация не вызывает CollectionsMarshal, запрещённый клиентской песочницей.
        return new List<PrototypeLayerData>
        {
            new()
            {
                RsiPath = rsi.Path.ToString(),
                State = state,
                Scale = appearance.Scale,
                Offset = appearance.Offset,
            },
        };
    }
}
