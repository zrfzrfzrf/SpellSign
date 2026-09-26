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
}
