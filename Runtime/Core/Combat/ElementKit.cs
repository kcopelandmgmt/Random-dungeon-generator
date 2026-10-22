using System.Collections.Generic;
using System.Linq;

namespace DungeonGen.Core.Combat
{
    /// <summary>
    /// One element's version of the Elementalist kit. All three share a skeleton (bolt, zone,
    /// mobility, passive) so the class is learnable once; the element decides what each one does.
    /// The element is whatever weapon the Elementalist holds, so losing the staff loses the spec.
    /// </summary>
    public sealed class ElementKit
    {
        public DamageType Element { get; }
        public string Name { get; }
        public string Tagline { get; }
        public string Bolt { get; }
        public string Zone { get; }
        public string Mobility { get; }
        public string Passive { get; }
        public StatusEffectDefinition OnHit => StatusEffectDefinition.OnHitFor(Element);
        public IReadOnlyList<string> Abilities => new[] { Bolt, Zone, Mobility, Passive };

        public ElementKit(DamageType element, string name, string tagline, string bolt, string zone, string mobility, string passive)
        {
            Element = element; Name = name; Tagline = tagline; Bolt = bolt; Zone = zone; Mobility = mobility; Passive = passive;
        }

        public static readonly IReadOnlyList<ElementKit> All = new[]
        {
            new ElementKit(DamageType.Fire, "Pyromancy", "Area denial. Burns rooms, not people.",
                "Firebolt: single target, applies Burn",
                "Firewall: 6 m line of fire for 6 s, blocks movement to anyone unwilling to burn",
                "Cinder Step: short teleport, leaves fire behind",
                "Kindling: Burn spreads to enemies within 2 m and stacks to three"),
            new ElementKit(DamageType.Frost, "Cryomancy", "Nobody leaves. Nobody arrives quickly either.",
                "Frost Shard: single target, applies Chill",
                "Glacial Field: 5 m circle for 6 s, Chills every second to anyone inside",
                "Ice Slide: dash that leaves a trail of ice; enemies crossing it are Chilled",
                "Deep Chill: three Chill stacks Freeze for 1 s; damage to a frozen target shatters it for 30% bonus"),
            new ElementKit(DamageType.Lightning, "Stormcalling", "The interrupt element. Casts end when you say so.",
                "Arc: single target, applies Stagger, chains to two enemies within 4 m",
                "Static Field: 5 m circle for 5 s, Staggers anyone entering it",
                "Blink: short teleport, no trail",
                "Conduction: Stagger on a Chilled target becomes a 1.5 s Stun"),
        };

        public static ElementKit For(DamageType element) => All.FirstOrDefault(k => k.Element == element);
    }
}
