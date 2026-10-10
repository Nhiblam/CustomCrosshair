namespace RoR2CustomCrosshair;

/// <summary>
/// How a layer's geometry is driven.
/// Dynamic means the layer may later be offset by actual weapon/projectile
/// spread only — never by charge, cooldown, windup, stock or other skill
/// state. No spread source exists yet, so both modes currently render the
/// configured geometry unchanged.
/// </summary>
internal enum CrosshairMode
{
    Static,
    Dynamic
}
