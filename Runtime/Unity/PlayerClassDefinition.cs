using DungeonGen.Core.Combat;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Designer-facing class asset. Create one per class (Create > Dungeon Gen > Player Class) or
    /// use the context menu to stamp one out from the built-in roster. Multipliers apply to
    /// <see cref="CombatBaseline"/>, so boss tuning and class balance share one set of numbers.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon Gen/Player Class", fileName = "PlayerClass")]
    public sealed class PlayerClassDefinition : ScriptableObject
    {
        public string ClassName = "Warden";
        public ClassRole Role = ClassRole.Tank;
        public Attributes BaseAttributes = Attributes.Baseline;
        public Attribute Primary = Attribute.Strength;
        [Range(0.4f, 1.4f)] public float KitCoefficient = 1f;
        public ArmourWeight Armour = ArmourWeight.Medium;
        [TextArea] public string Tagline;
        public string[] Abilities = new string[4];
        [TextArea] public string DungeonUtility;

        [Header("Fill from roster")]
        [Range(0, 5)] public int RosterIndex;

        /// <summary>Full derived sheet at a gear tier.</summary>
        public PlayerStats StatsAt(int gearTier) =>
            PlayerStats.For(new ClassArchetype(ClassName, Role, BaseAttributes, Primary, KitCoefficient, Armour, Tagline, Abilities, DungeonUtility), gearTier);

        [ContextMenu("Fill From Default Roster")]
        public void FillFromRoster()
        {
            var a = ClassArchetype.DefaultRoster[Mathf.Clamp(RosterIndex, 0, ClassArchetype.DefaultRoster.Count - 1)];
            ClassName = a.Name;
            Role = a.Role;
            BaseAttributes = a.BaseAttributes;
            Primary = a.Primary;
            KitCoefficient = a.KitCoefficient;
            Armour = a.Armour;
            Tagline = a.Tagline;
            Abilities = new string[a.Abilities.Count];
            for (int i = 0; i < a.Abilities.Count; i++) Abilities[i] = a.Abilities[i];
            DungeonUtility = a.DungeonUtility;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
