using Meitou.Engine.Input;

namespace Meitou.Tests.Engine;

public class InputTests
{
    readonly InputState input = new();
    readonly InputBindings bindings = new();

    ActionState Tick() => bindings.Resolve(input.Consume());

    [Fact]
    public void A_press_is_seen_by_exactly_one_of_several_ticks_in_a_frame()
    {
        input.SetKey(Key.W, true);
        var t1 = Tick();
        var t2 = Tick();
        var t3 = Tick();
        Assert.True(t1.Pressed(InputAction.MoveForward));
        Assert.False(t2.Pressed(InputAction.MoveForward));
        Assert.False(t3.Pressed(InputAction.MoveForward));
        Assert.True(t1.Held(InputAction.MoveForward) && t2.Held(InputAction.MoveForward) && t3.Held(InputAction.MoveForward));
    }

    [Fact]
    public void F5_quicksaves_and_F9_quickloads_and_the_fps_counter_has_no_plain_key()
    {
        Assert.Equal([Binding.Of(Key.F5)], bindings.Get(InputAction.QuickSave));
        Assert.Equal([Binding.Of(Key.F9)], bindings.Get(InputAction.QuickLoad));
        Assert.Empty(bindings.Get(InputAction.ToggleFps));
        input.SetKey(Key.F9, true);
        var t = Tick();
        Assert.True(t.Pressed(InputAction.QuickLoad));
        Assert.False(t.Pressed(InputAction.QuickSave) || t.Pressed(InputAction.ToggleFps));
    }
}
