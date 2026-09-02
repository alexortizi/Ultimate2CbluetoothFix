using BitDoFixer.Services;
using Xunit;

namespace BitDoFixer.Tests;

public class Xbox360MappingTests
{
    // --- NormalizeAxis: DirectInput entrega 0..65535 con centro en 32767 ---

    [Theory]
    [InlineData(32767, 0)]          // centro
    [InlineData(0, -32767)]         // extremo bajo
    [InlineData(65534, 32767)]      // extremo alto representable
    [InlineData(65535, 32767)]      // 32768 no cabe en short: se clampea
    [InlineData(40000, 7233)]
    public void NormalizeAxis_CentersAndClamps(int raw, short expected)
    {
        Assert.Equal(expected, Xbox360Mapping.NormalizeAxis(raw));
    }

    // --- ApplyDeadzone: banda muerta abierta de +-4000 ---

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3999, 0)]
    [InlineData(-3999, 0)]
    [InlineData(4000, 4000)]        // el borde NO esta en la banda muerta
    [InlineData(-4000, -4000)]
    [InlineData(30000, 30000)]
    public void ApplyDeadzone_ZeroesOnlyInsideTheBand(short input, short expected)
    {
        Assert.Equal(expected, Xbox360Mapping.ApplyDeadzone(input));
    }

    [Fact]
    public void Deadzone_ConstantIsUnchangedFromTheOriginalRemapper()
    {
        Assert.Equal(4000, Xbox360Mapping.Deadzone);
    }

    // --- NegateAxis: short.MinValue no tiene negativo representable ---

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, -100)]
    [InlineData(-100, 100)]
    [InlineData(short.MaxValue, -32767)]
    public void NegateAxis_Negates(short input, short expected)
    {
        Assert.Equal(expected, Xbox360Mapping.NegateAxis(input));
    }

    [Fact]
    public void NegateAxis_ClampsMinValueInsteadOfOverflowing()
    {
        Assert.Equal(short.MaxValue, Xbox360Mapping.NegateAxis(short.MinValue));
    }

    // --- GetBtn: tolera null y fuera de rango ---

    [Fact]
    public void GetBtn_ReturnsFalseForNullArray()
    {
        Assert.False(Xbox360Mapping.GetBtn(null, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void GetBtn_ReturnsFalseOutOfRange(int index)
    {
        Assert.False(Xbox360Mapping.GetBtn(new[] { true, true, true }, index));
    }

    [Fact]
    public void GetBtn_ReturnsTheValueInRange()
    {
        var buttons = new[] { false, true, false };
        Assert.False(Xbox360Mapping.GetBtn(buttons, 0));
        Assert.True(Xbox360Mapping.GetBtn(buttons, 1));
        Assert.False(Xbox360Mapping.GetBtn(buttons, 2));
    }

    // --- PovToDpad: el POV viene en centesimas de grado, -1 = centrado ---

    [Fact]
    public void PovToDpad_CenteredWhenNull()
    {
        Assert.Equal(new DpadState(false, false, false, false), Xbox360Mapping.PovToDpad(null));
    }

    [Fact]
    public void PovToDpad_CenteredWhenEmpty()
    {
        Assert.Equal(new DpadState(false, false, false, false), Xbox360Mapping.PovToDpad(Array.Empty<int>()));
    }

    [Fact]
    public void PovToDpad_CenteredWhenNegative()
    {
        Assert.Equal(new DpadState(false, false, false, false), Xbox360Mapping.PovToDpad(new[] { -1 }));
    }

    [Theory]
    // pov,          up,    right, down,  left
    [InlineData(0,     true,  false, false, false)]  // arriba
    [InlineData(9000,  false, true,  false, false)]  // derecha
    [InlineData(18000, false, false, true,  false)]  // abajo
    [InlineData(27000, false, false, false, true)]   // izquierda
    public void PovToDpad_CardinalDirections(int pov, bool up, bool right, bool down, bool left)
    {
        Assert.Equal(new DpadState(up, right, down, left), Xbox360Mapping.PovToDpad(new[] { pov }));
    }

    [Theory]
    // Los bordes de los rangos son inclusivos en los dos lados, asi que las
    // diagonales exactas activan las dos direcciones. Es intencional: 4500 son
    // 45 grados, o sea arriba-derecha.
    [InlineData(4500,  true,  true,  false, false)]
    [InlineData(13500, false, true,  true,  false)]
    [InlineData(22500, false, false, true,  true)]
    [InlineData(31500, true,  false, false, true)]
    public void PovToDpad_ExactDiagonalsSetBothDirections(int pov, bool up, bool right, bool down, bool left)
    {
        Assert.Equal(new DpadState(up, right, down, left), Xbox360Mapping.PovToDpad(new[] { pov }));
    }

    [Fact]
    public void PovToDpad_WrapsPastTheTop()
    {
        // 35999 (~360 grados) sigue siendo "arriba"
        Assert.Equal(new DpadState(true, false, false, false), Xbox360Mapping.PovToDpad(new[] { 35999 }));
    }

    [Fact]
    public void PovToDpad_OnlyReadsTheFirstHat()
    {
        Assert.Equal(new DpadState(true, false, false, false), Xbox360Mapping.PovToDpad(new[] { 0, 18000 }));
    }
}
