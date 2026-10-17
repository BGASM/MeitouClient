using System.Runtime.CompilerServices;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

/// <summary>The C# structs the character shaders read must match the GLSL layouts byte for byte (std430 / vertex rows / push constants).</summary>
public class CharacterLayoutTests
{
    [Fact]
    public void Instance_is_144_bytes() => Assert.Equal(CharInstance.Size, Unsafe.SizeOf<CharInstance>());

    [Fact]
    public void Material_record_is_256_bytes() => Assert.Equal(CharacterMaterialRecord.Size, Unsafe.SizeOf<CharacterMaterialRecord>());
}
