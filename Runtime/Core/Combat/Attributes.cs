using System;

namespace DungeonGen.Core.Combat
{
    public enum Attribute { Strength, Dexterity, Intelligence, Constitution, Spirit }

    /// <summary>
    /// The five primary attributes. 10 is an average adventurer; a class starts with 50 points
    /// spread across the five, and levels and gear add to them. Everything else is derived.
    /// </summary>
    [Serializable]
    public struct Attributes
    {
        public int Strength;      // melee damage, carry capacity, heavy armour
        public int Dexterity;     // ranged / finesse damage, attack speed, crit, move speed, stamina
        public int Intelligence;  // spell damage, mana
        public int Constitution;  // health
        public int Spirit;        // healing power, resource regen, magic resistance

        public Attributes(int str, int dex, int intel, int con, int spi)
        {
            Strength = str; Dexterity = dex; Intelligence = intel; Constitution = con; Spirit = spi;
        }

        public static readonly Attributes Baseline = new Attributes(10, 10, 10, 10, 10);

        public int Get(Attribute a)
        {
            switch (a)
            {
                case Attribute.Strength: return Strength;
                case Attribute.Dexterity: return Dexterity;
                case Attribute.Intelligence: return Intelligence;
                case Attribute.Constitution: return Constitution;
                default: return Spirit;
            }
        }

        public int Total => Strength + Dexterity + Intelligence + Constitution + Spirit;

        public static Attributes operator +(Attributes a, Attributes b) =>
            new Attributes(a.Strength + b.Strength, a.Dexterity + b.Dexterity, a.Intelligence + b.Intelligence, a.Constitution + b.Constitution, a.Spirit + b.Spirit);

        public override string ToString() => $"STR {Strength} DEX {Dexterity} INT {Intelligence} CON {Constitution} SPI {Spirit}";
    }
}
