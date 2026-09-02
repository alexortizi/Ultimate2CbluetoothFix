namespace BitDoFixer.Services;

/// <summary>Estado del D-pad derivado del hat switch. Inmutable y comparable.</summary>
internal readonly record struct DpadState(bool Up, bool Right, bool Down, bool Left);

/// <summary>
/// Traducción pura DirectInput -> Xbox360. Sin estado, sin COM, sin hardware:
/// es la única parte del mapeo que se puede testear sin un mando enchufado.
/// Extraído de BluetoothRemapper sin cambios de comportamiento.
/// </summary>
internal static class Xbox360Mapping
{
    public const int Deadzone = 4000;

    /// <summary>Los ejes de SharpDX llegan como 0..65535 con el centro en 32767.</summary>
    public static short NormalizeAxis(int v)
    {
        int centered = v - 32767;
        if (centered < short.MinValue) centered = short.MinValue;
        if (centered > short.MaxValue) centered = short.MaxValue;
        return (short)centered;
    }

    public static short ApplyDeadzone(short v)
    {
        if (v > -Deadzone && v < Deadzone) return 0;
        return v;
    }

    /// <summary>El negativo de short.MinValue no es representable: se clampea.</summary>
    public static short NegateAxis(short v)
    {
        if (v == short.MinValue) return short.MaxValue;
        return (short)-v;
    }

    public static bool GetBtn(bool[]? buttons, int index)
    {
        if (buttons is null) return false;
        if (index < 0 || index >= buttons.Length) return false;
        return buttons[index];
    }

    /// <summary>
    /// El POV llega en centésimas de grado (0 = arriba, sentido horario); negativo
    /// significa centrado. Los rangos son inclusivos en los dos extremos, así que
    /// una diagonal exacta activa las dos direcciones que la componen.
    /// </summary>
    public static DpadState PovToDpad(int[]? povs)
    {
        if (povs is null || povs.Length == 0) return default;

        int pov = povs[0];
        if (pov < 0) return default;

        bool up    = pov >= 31500 || pov <= 4500;
        bool right = pov >= 4500  && pov <= 13500;
        bool down  = pov >= 13500 && pov <= 22500;
        bool left  = pov >= 22500 && pov <= 31500;

        return new DpadState(up, right, down, left);
    }
}
