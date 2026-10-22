public enum Element { Water, Fire, Lightning }

// Water beats Fire, Fire beats Lightning, Lightning beats Water.
public static class ElementRules
{
    public const int Immune = 0;
    public const int Normal = 1;
    public const int Effective = 2;

    public static int Damage(Element attack, Element defender)
    {
        if (attack == defender) return Immune;
        return Beats(attack, defender) ? Effective : Normal;
    }

    static bool Beats(Element a, Element b) =>
        (a == Element.Water && b == Element.Fire) ||
        (a == Element.Fire && b == Element.Lightning) ||
        (a == Element.Lightning && b == Element.Water);

    // The element that beats this one.
    public static Element Counter(Element defender)
    {
        switch (defender)
        {
            case Element.Fire:      return Element.Water;
            case Element.Lightning: return Element.Fire;
            default:                return Element.Lightning;
        }
    }
}

// How each element looks and is called wherever the game shows one: the same
// colors as the dragon's attacks and the glow on the hands. Name is the
// element's fixed English name, for files and objects; DisplayName is what the
// player reads, in their language.
public static class ElementLook
{
    public static UnityEngine.Color Color(Element element)
    {
        switch (element)
        {
            case Element.Water: return new UnityEngine.Color(0.2f, 0.5f, 1f);
            case Element.Fire:  return new UnityEngine.Color(1f, 0.5f, 0.1f);
            default:            return new UnityEngine.Color(1f, 0.9f, 0.2f);
        }
    }

    public static string Name(Element element)
    {
        switch (element)
        {
            case Element.Water: return "Water";
            case Element.Fire:  return "Fire";
            default:            return "Lightning";
        }
    }

    public static string DisplayName(Element element) => Loc.T("element." + element);
}

// Anything a player's spell can hit: the dragon (EnemyTarget) and the
// tutorial's crystals (ElementCrystal). Spells find it on the collider they
// struck or one of its parents.
public interface ISpellTarget
{
    // hitPoint: where the spell struck. struck: the collider it struck.
    void TakeHit(Element attack, UnityEngine.Vector3? hitPoint = null, UnityEngine.Collider struck = null);
}
